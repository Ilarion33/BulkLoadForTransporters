// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadTransporters/Toil_TakeToCarry.cs
using BulkLoadForTransporters.Core.Interfaces;
using Verse;
using Verse.AI;
using UnityEngine; // For Mathf

namespace BulkLoadForTransporters.Toils_LoadTransporters
{
    /// <summary>
    /// A static class containing the factory method for the "take to carry" Toil.
    /// </summary>
    public static class Toil_TakeToCarry
    {
        /// <summary>
        /// Creates a Toil that makes the pawn pick up a target Thing and hold it.
        /// </summary>
        /// <param name="index">The TargetIndex where the Thing to be picked up is stored.</param>
        /// <param name="haulState">The JobDriver's state interface for tracking hauled items.</param>
        /// <returns>A configured Toil ready to be used in a JobDriver.</returns>
        public static Toil Create(TargetIndex index, IBulkHaulState haulState)
        {
            Toil toil = ToilMaker.MakeToil("TakeToCarry");
            toil.initAction = () =>
            {
                Pawn actor = toil.actor;
                Job curJob = actor.CurJob;
                Thing thingToPickUp = curJob.GetTarget(index).Thing;

                if (thingToPickUp == null || thingToPickUp.Destroyed)
                {
                    actor.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                    return;
                }

                // 从Job的count字段中读取规划好的拾取数量。
                int plannedCount = curJob.count;
                int countToPickUp = Mathf.Min(plannedCount, thingToPickUp.stackCount);

                if (countToPickUp <= 0) return;

                // NOTE: 在释放预定前，先检查我们是否真的拥有它。
                if (actor.Map.reservationManager.ReservedBy(thingToPickUp, actor, curJob))
                {
                    actor.Map.reservationManager.Release(thingToPickUp, actor, curJob);
                }

                Thing itemToCarry = thingToPickUp.SplitOff(countToPickUp);

                // 尝试将新分离出的物品放入小人手中。
                if (actor.carryTracker.TryStartCarry(itemToCarry))
                {
                    haulState.AddHauledThing(actor.carryTracker.CarriedThing);
                }
                else
                {
                    Log.Error($"[BulkLoad] Failed to carry {itemToCarry.LabelCap}. Placing back on ground.");
                    GenPlace.TryPlaceThing(itemToCarry, actor.Position, actor.Map, ThingPlaceMode.Near);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}