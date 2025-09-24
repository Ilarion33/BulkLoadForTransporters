// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_UnloadCarriers/Toil_TransferFromContainer.cs
using BulkLoadForTransporters.Core.Interfaces;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_UnloadCarriers
{
    /// <summary>
    /// A utility class for creating the item transfer Toil.
    /// </summary>
    public static class Toil_TransferFromContainer
    {
        /// <summary>
        /// Creates a Toil that transfers a specific item from a container to the pawn.
        /// </summary>
        public static Toil Create(TargetIndex containerInd, TargetIndex itemToTakeInd, IBulkHaulState haulState)
        {
            Toil toil = ToilMaker.MakeToil("TransferFromContainer_Simple");
            toil.initAction = () =>
            {
                Pawn pawn = toil.actor;
                Job job = pawn.CurJob;
                Thing containerThing = job.GetTarget(containerInd).Thing;
                Thing thingToTake = job.GetTarget(itemToTakeInd).Thing;

                var sourceOwner = containerThing?.TryGetInnerInteractableThingOwner();
                if (sourceOwner == null || thingToTake == null || !sourceOwner.Contains(thingToTake))
                {
                    return;
                }

                // NOTE: 这个Toil是“无脑的”，它不自己做决策。
                // 它完全信任上一个Toil(SelectNextItem)已经将正确的指令
                int countToTake = job.count;
                bool takeToCarryTracker = job.haulOpportunisticDuplicates;

                if (countToTake <= 0) return;

                ThingOwner targetOwner = takeToCarryTracker ? pawn.carryTracker.innerContainer : pawn.inventory.innerContainer;

                // 直接调用TryTransferToContainer并依赖其out参数来更新状态。
                sourceOwner.TryTransferToContainer(thingToTake, targetOwner, countToTake, out Thing transferredThing);

                if (transferredThing != null)
                {
                    haulState.AddHauledThing(transferredThing);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}