// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_DeliverConstruction/Toil_DepositItemForConstruction.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using BulkLoadForTransporters.Jobs;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_DeliverConstruction
{
    public static class Toil_DepositItemForConstruction
    {
        public static Toil Create(IManagedLoadable managedLoadable)
        {
            Toil toil = ToilMaker.MakeToil("DepositItemForConstruction");
            toil.initAction = () =>
            {
                var driver = toil.actor.jobs.curDriver as JobDriver_DeliverConstructionInBulk;
                if (driver == null) return;

                Pawn pawn = toil.actor;
                Thing carriedThing = pawn.carryTracker.CarriedThing;
                var jobTarget = driver.job.targetB.Thing;

                DebugLogger.LogMessage(LogCategory.Toils, () => $"{pawn.LabelShort} is executing deposit into Construction Site.");

                if (carriedThing == null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => "-> Toil ABORTED: Pawn is not carrying anything.");
                    return;
                }

                var targetContainer = jobTarget.TryGetInnerInteractableThingOwner();
                if (targetContainer == null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"-> Toil FAILED: Could not get inner container for '{jobTarget?.LabelCap}'.");
                    return;
                }
                
                var remainingNeeds = driver._unloadRemainingNeeds;
                if (remainingNeeds == null)
                {
                    Log.Error("[BulkLoad] Deposit Toil FAILED: driver._unloadRemainingNeeds is NULL! This indicates a bug in BeginUnloadSession.");
                    return;
                }

                
                remainingNeeds.TryGetValue(carriedThing.def, out int needed);
                if (needed < 0) needed = 0;

                int amountToDeposit = Mathf.Min(carriedThing.stackCount, needed);
                DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Carried: {carriedThing.LabelCap} (x{carriedThing.stackCount}). Needed: {needed}. Will deposit: {amountToDeposit}.");

                if (amountToDeposit > 0)
                {
                    // --- 关键变更：完全采纳 Portal Toil 的健壮逻辑 ---
                    Global_Utility.IsExecutingManagedUnload = true;
                    try
                    {
                        // 1. 在物品被消耗（并销毁）之前，安全地记录其核心信息。
                        ThingDef defToLoad = carriedThing.def;

                        // 2. 执行物理操作，并忽略其不可靠的 out 参数。
                        pawn.carryTracker.innerContainer.TryTransferToContainer(carriedThing, targetContainer, amountToDeposit, out _);

                        // 3. 使用我们事前记录的、100% 可靠的数据来更新所有逻辑系统。
                        DebugLogger.LogMessage(LogCategory.Toils, () => $"  - TransferToContainer executed for {defToLoad.defName} x{amountToDeposit}.");

                        CentralLoadManager.Instance?.Notify_ItemLoaded(pawn, managedLoadable, defToLoad, amountToDeposit);

                        if (remainingNeeds != null && remainingNeeds.ContainsKey(defToLoad))
                        {
                            remainingNeeds[defToLoad] -= amountToDeposit;
                            DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Session needs updated for {defToLoad.defName}. New need: {remainingNeeds[defToLoad]}.");
                        }
                    }
                    finally
                    {
                        Global_Utility.IsExecutingManagedUnload = false;
                    }
                }

                // --- 剩余物处理 ---
                var surplus = pawn.carryTracker.CarriedThing;
                if (surplus != null) 
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Surplus detected: {surplus.LabelCap} (x{surplus.stackCount}). Processing...");
                    
                    int countToStoreInInventory = Global_Utility.CalculateSurplusToStoreInInventory(pawn, surplus);

                    DebugLogger.LogMessage(LogCategory.Toils, () => $"    - Pawn can store {countToStoreInInventory} of surplus in inventory.");

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
    }
}