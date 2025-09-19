// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadTransporters/Toil_TakeToCarry.cs
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using UnityEngine; // For Mathf
using Verse;
using Verse.AI;

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
                DebugLogger.LogMessage(LogCategory.Toils, () => $"{actor.LabelShort} is taking {thingToPickUp?.LabelCap} to CARRY.");

                if (thingToPickUp == null || thingToPickUp.Destroyed)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => "-> Toil FAILED: Thing to pick up is null or destroyed.");
                    actor.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                    return;
                }

                // 从Job的count字段中读取规划好的拾取数量。
                int plannedCount = curJob.count;
                int countToPickUp = Mathf.Min(plannedCount, thingToPickUp.stackCount);
                DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Planned: {plannedCount}, On ground: {thingToPickUp.stackCount}. Will take: {countToPickUp}.");

                if (countToPickUp <= 0)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => "-> Toil ABORTED: Calculated count to pick up is zero.");
                    return;
                }

                // NOTE: 在释放预定前，先检查我们是否真的拥有它。
                if (actor.Map.reservationManager.ReservedBy(thingToPickUp, actor, curJob))
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Releasing reservation on {thingToPickUp.LabelCap}.");
                    actor.Map.reservationManager.Release(thingToPickUp, actor, curJob);
                }

                Thing itemToCarry = thingToPickUp.SplitOff(countToPickUp);

                // 尝试将新分离出的物品放入小人手中。
                if (actor.carryTracker.TryStartCarry(itemToCarry))
                {
                    haulState.AddHauledThing(actor.carryTracker.CarriedThing);
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Successfully carried {actor.carryTracker.CarriedThing.LabelCap}. HauledThings count: {haulState.HauledThings.Count}.");
                }
                else
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - TryStartCarry FAILED for {itemToCarry.LabelCap}. Dropping on ground.");
                    Log.Error($"[BulkLoad] Failed to carry {itemToCarry.LabelCap}. Placing back on ground.");
                    GenPlace.TryPlaceThing(itemToCarry, actor.Position, actor.Map, ThingPlaceMode.Near);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}