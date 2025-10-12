// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadPortal/Toil_DepositItemForPortal.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using BulkLoadForTransporters.Jobs;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_LoadPortal
{
    /// <summary>
    /// A utility class for creating the Toil sequence to deposit an item into a MapPortal.
    /// </summary>
    public static class Toil_DepositItemForPortal
    {
        /// <summary>
        /// Creates a sequence of Toils that waits for a duration and then deposits the carried item.
        /// </summary>
        /// <returns>An IEnumerable of Toils representing the deposit sequence.</returns>
        public static IEnumerable<Toil> Create(IManagedLoadable managedLoadable)
        {
            // NOTE: 这个90 tick的延迟是镜像自原版的JobDriver_HaulToPortal，以保持行为一致性。
            const int DepositDuration = 90;

            // Toil 1: 等待并显示进度条，模拟将物品放入传送门的耗时。
            Toil waitToil = Toils_General.Wait(DepositDuration, TargetIndex.B)
                .WithProgressBarToilDelay(TargetIndex.B, false, -0.5f);

            waitToil.initAction = () => {
                DebugLogger.LogMessage(LogCategory.Toils, () => $"{waitToil.actor.LabelShort} is waiting ({DepositDuration} ticks) before depositing into Portal.");
            };

            yield return waitToil;

            // Toil 2: 实际执行传送操作。
            Toil depositToil = ToilMaker.MakeToil("DepositItemToPortal_Hardened");
            depositToil.initAction = () =>
            {
                var driver = depositToil.actor.jobs.curDriver as JobDriver_BulkLoadBase;
                if (driver == null) return;
                Pawn pawn = depositToil.actor;
                DebugLogger.LogMessage(LogCategory.Toils, () => $"{pawn.LabelShort} is executing deposit into Portal.");
                Thing carriedThing = pawn.carryTracker.CarriedThing;
                if (carriedThing == null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => "-> Toil ABORTED: Pawn is not carrying anything.");
                    return;
                }

                Thing jobTarget = pawn.CurJob.targetB.Thing;
                var targetContainer = managedLoadable.GetInnerContainerFor(jobTarget);
                if (targetContainer == null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"-> Toil FAILED: Could not get inner container for '{jobTarget?.LabelCap}'.");
                    return;
                }

                // 与 Toil_DepositItem 保持一致，我们也考虑会话状态中的剩余需求
                var transferables = driver._unloadTransferables;
                var remainingNeeds = driver._unloadRemainingNeeds;
                int needed = JobDriver_Utility.ResolveNeededAmountForUnload(carriedThing, transferables, remainingNeeds);
                int amountToDeposit = Mathf.Min(carriedThing.stackCount, needed);

                DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Carried: {carriedThing.LabelCap} (x{carriedThing.stackCount}). Needed: {needed}. Will deposit: {amountToDeposit}.");
                if (amountToDeposit > 0)
                {
                    Global_Utility.IsExecutingManagedUnload = true;
                    try
                    {
                        // 在物品被传送（并可能被销毁）之前，安全地记录其核心信息。
                        ThingDef defToLoad = carriedThing.def;

                        pawn.carryTracker.innerContainer.TryTransferToContainer(carriedThing, targetContainer, amountToDeposit, out _);

                        DebugLogger.LogMessage(LogCategory.Toils, () => $"  - TransferToContainer executed for {defToLoad.defName} x{amountToDeposit}.");
                        // 使用不依赖Thing实例的API来通知管理器，完成账目结算。
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

                // 检查卸货后手上是否还有剩余物品（溢出物）。
                var surplus = pawn.carryTracker.CarriedThing;
                if (surplus != null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Surplus detected: {surplus.LabelCap} (x{surplus.stackCount}). Dropping on ground.");

                    // 无论任何原因，只要有剩余，就全部丢在地上。
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing _);
                }
            };
            depositToil.defaultCompleteMode = ToilCompleteMode.Instant;

            yield return depositToil;
        }
               
    }
}