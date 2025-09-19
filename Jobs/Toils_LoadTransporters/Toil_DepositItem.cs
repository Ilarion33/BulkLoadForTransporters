// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadTransporters/Toil_DepositItem.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using BulkLoadForTransporters.Jobs;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_LoadTransporters
{
    /// <summary>
    /// Provides a Toil that deposits a carried Thing into a container that uses the IManagedLoadable interface.
    /// It's designed for standard containers like transport pods.
    /// </summary>
    public static class Toil_DepositItem
    {
        public static Toil Create(IManagedLoadable managedLoadable)
        {
            Toil toil = ToilMaker.MakeToil("DepositItem");
            toil.initAction = () =>
            {
                var driver = toil.actor.jobs.curDriver as JobDriver_LoadTransportersInBulk;
                if (driver == null) return;
                Pawn pawn = toil.actor;
                DebugLogger.LogMessage(LogCategory.Toils, () => $"{pawn.LabelShort} is executing deposit into Transporter.");
                Job curJob = pawn.CurJob;
                Thing carriedThing = pawn.carryTracker.CarriedThing;
                if (carriedThing == null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => "-> Toil ABORTED: Pawn is not carrying anything.");
                    return;
                }

                // 通过 Adapter 获取最终要放入的物理容器。
                Thing jobTarget = curJob.targetB.Thing;
                var targetContainer = managedLoadable.GetInnerContainerFor(jobTarget);
                if (targetContainer == null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"-> Toil FAILED: Could not get inner container for '{jobTarget?.LabelCap}'.");
                    return;
                }

                // 从驱动的会话状态中获取当前卸货任务的精确需求。
                var transferables = driver._unloadTransferables;
                var remainingNeeds = driver._unloadRemainingNeeds;

                // 计算实际需要存入的数量，防止因玩家中途修改清单而过量装载。
                int needed = GetCurrentNeededAmountFor(transferables, carriedThing, remainingNeeds);
                int amountToDeposit = Mathf.Min(carriedThing.stackCount, needed);

                DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Carried: {carriedThing.LabelCap} (x{carriedThing.stackCount}). Needed: {needed}. Will deposit: {amountToDeposit}.");

                if (amountToDeposit > 0)
                {
                    BulkLoad_Utility.IsExecutingManagedUnload = true;
                    try
                    {
                        Thing depositedThing;
                        pawn.carryTracker.innerContainer.TryTransferToContainer(carriedThing, targetContainer, amountToDeposit, out depositedThing);

                        if (depositedThing != null)
                        {
                            DebugLogger.LogMessage(LogCategory.Toils, () => $"  - TransferToContainer SUCCEEDED. Deposited: {depositedThing.LabelCap} (x{depositedThing.stackCount}).");
                            // 通知中央管理器，物品已成功装载，以便更新全局的并发记账。
                            CentralLoadManager.Instance?.Notify_ItemLoaded(pawn, managedLoadable, depositedThing);
                            if (remainingNeeds != null && remainingNeeds.ContainsKey(depositedThing.def))
                            {
                                remainingNeeds[depositedThing.def] -= depositedThing.stackCount;
                                DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Session needs updated for {depositedThing.def.defName}. New need: {remainingNeeds[depositedThing.def]}.");
                            }
                        }
                        else
                        {
                            DebugLogger.LogMessage(LogCategory.Toils, () => $"  - TransferToContainer FAILED or resulted in null thing.");
                        }
                    }
                    finally
                    {
                        BulkLoad_Utility.IsExecutingManagedUnload = false;
                    }
                }

                // 检查卸货后手上是否还有剩余物品（溢出物）。
                var surplus = pawn.carryTracker.CarriedThing;
                if (surplus != null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Surplus detected: {surplus.LabelCap} (x{surplus.stackCount}). Processing...");
                    // 检查背包剩余负重，决定能将多少剩余物安全地放回背包。
                    float availableMass = MassUtility.FreeSpace(pawn);
                    float massPerItem = surplus.GetStatValue(StatDefOf.Mass);
                    int countToStoreInInventory = (availableMass > 0 && massPerItem > 0) ? Mathf.FloorToInt(availableMass / massPerItem) : 0;
                    countToStoreInInventory = Mathf.Min(surplus.stackCount, countToStoreInInventory);
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"    - Pawn has {availableMass:F2}kg free space. Can store {countToStoreInInventory} of surplus in inventory.");

                    if (countToStoreInInventory > 0)
                    {
                        // 从手上“拿走”可以放入背包的部分。
                        Thing portionForInventory = pawn.carryTracker.innerContainer.Take(surplus, countToStoreInInventory);

                        // NOTE: canMerge 设置为 false 确保任务物品（剩余物）不会与小人自己的个人物品合并。
                        if (portionForInventory != null && pawn.inventory.innerContainer.TryAdd(portionForInventory, false))
                        {
                            driver.SurplusThings.Add(portionForInventory);
                            DebugLogger.LogMessage(LogCategory.Toils, () => $"    - Stored {portionForInventory.LabelCap} in inventory as surplus.");
                        }
                        else if (portionForInventory != null)
                        {
                            // 如果添加失败，则丢在地上。
                            GenPlace.TryPlaceThing(portionForInventory, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                            DebugLogger.LogMessage(LogCategory.Toils, () => $"    - Failed to add to inventory, dropped {portionForInventory.LabelCap} on ground.");
                        }
                    }

                    // 再次检查手上，如果还有剩余（因为负重不足），则全部丢在地上。
                    if (pawn.carryTracker.CarriedThing != null)
                    {
                        DebugLogger.LogMessage(LogCategory.Toils, () => $"    - Remaining surplus on hand ({pawn.carryTracker.CarriedThing.LabelCap}) exceeds capacity. Dropping.");
                        pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing _);
                    }
                }            
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        /// <summary>
        /// A private helper to calculate the precise number of a thing still needed for the current unload session.
        /// </summary>
        private static int GetCurrentNeededAmountFor(List<TransferableOneWay> transferables, Thing thing, Dictionary<ThingDef, int> remainingNeeds)
        {
            if (transferables == null || thing == null || remainingNeeds == null) return 0;
            if (!remainingNeeds.TryGetValue(thing.def, out int totalNeeded) || totalNeeded <= 0) return 0;
            var bestMatch = BulkLoad_Utility.FindBestMatchFor(thing, transferables);
            if (bestMatch != null) return Mathf.Min(bestMatch.CountToTransfer, totalNeeded);
            return 0;
        }
    }
}