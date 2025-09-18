// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadPortal/Toil_DepositItemForPortal.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Jobs.Toils_LoadPortal
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

            yield return waitToil;

            // Toil 2: 实际执行传送操作。
            Toil depositToil = ToilMaker.MakeToil("DepositItemToPortal_Hardened");
            depositToil.initAction = () =>
            {
                var driver = depositToil.actor.jobs.curDriver as JobDriver_BulkLoadBase;
                if (driver == null) return;
                Pawn pawn = depositToil.actor;
                Thing carriedThing = pawn.carryTracker.CarriedThing;
                if (carriedThing == null) return;

                Thing jobTarget = pawn.CurJob.targetB.Thing;
                var targetContainer = managedLoadable.GetInnerContainerFor(jobTarget);
                if (targetContainer == null) return;

                // 与 Toil_DepositItem 保持一致，我们也考虑会话状态中的剩余需求
                var transferables = driver._unloadTransferables;
                var remainingNeeds = driver._unloadRemainingNeeds;
                int needed = GetCurrentNeededAmountFor(transferables, carriedThing, remainingNeeds);
                int amountToDeposit = Mathf.Min(carriedThing.stackCount, needed);

                if (amountToDeposit > 0)
                {
                    BulkLoad_Utility.IsExecutingManagedUnload = true;
                    try
                    {
                        // 在物品被传送（并可能被销毁）之前，安全地记录其核心信息。
                        ThingDef defToLoad = carriedThing.def;

                        pawn.carryTracker.innerContainer.TryTransferToContainer(carriedThing, targetContainer, amountToDeposit, out _);

                        // 使用不依赖Thing实例的API来通知管理器，完成账目结算。
                        CentralLoadManager.Instance?.Notify_ItemLoaded(pawn, managedLoadable, defToLoad, amountToDeposit);

                        if (remainingNeeds != null && remainingNeeds.ContainsKey(defToLoad))
                        {
                            remainingNeeds[defToLoad] -= amountToDeposit;
                        }
                    }
                    finally
                    {
                        BulkLoad_Utility.IsExecutingManagedUnload = false;
                    }
                }
            };
            depositToil.defaultCompleteMode = ToilCompleteMode.Instant;

            yield return depositToil;
        }

        /// <summary>
        /// A private helper to calculate the remaining amount needed for a specific Thing.
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