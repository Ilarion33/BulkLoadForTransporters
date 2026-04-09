// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadTransporters/Toil_TakeToCarry.cs
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_LoadTransporters
{
    /// <summary>
    /// A static class containing the factory method for the "take to carry" Toil.
    /// </summary>
    public static class Toil_TakeToCarry
    {
        public static Toil Create(TargetIndex index, IBulkHaulState haulState)
        {
            Toil toil = ToilMaker.MakeToil("TakeToCarry_UpgradedAndHardened");
            toil.initAction = () =>
            {
                Pawn actor = toil.actor;
                Job curJob = actor.CurJob;
                Thing thingToPickUp = curJob.GetTarget(index).Thing;

                // --- 在所有操作之前，进行严格的有效性检查 ---
                if (thingToPickUp == null || thingToPickUp.Destroyed)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => "-> Toil FAILED: Thing to pick up is null or destroyed.");
                    actor.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                    return;
                }

                Thing carriedThing = actor.carryTracker.CarriedThing;
                if (carriedThing != null &&
                    carriedThing.def == thingToPickUp.def &&
                    carriedThing.CanStackWith(thingToPickUp))
                {
                    // --- “合并”分支 ---
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"{actor.LabelShort} is MERGING {thingToPickUp.LabelCap} into carried thing.");

                    int countToTake = Mathf.Min(curJob.count, thingToPickUp.stackCount);
                    int spaceInHands = carriedThing.def.stackLimit - carriedThing.stackCount;
                    countToTake = Mathf.Min(countToTake, spaceInHands);

                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Planned: {curJob.count}, On ground: {thingToPickUp.stackCount}, Space in hands: {spaceInHands}. Will merge: {countToTake}.");

                    if (countToTake <= 0)
                    {
                        DebugLogger.LogMessage(LogCategory.Toils, () => "-> Toil ABORTED: No space in hands to merge.");
                        return;
                    }

                    Thing chunkToAbsorb = thingToPickUp.SplitOff(countToTake);

                    if (!carriedThing.TryAbsorbStack(chunkToAbsorb, true))
                    {
                        Log.Error($"[BulkLoad] TakeToCarry merge failed unexpectedly. Placing chunk on ground to prevent loss.");
                        GenPlace.TryPlaceThing(chunkToAbsorb, actor.Position, actor.Map, ThingPlaceMode.Near);
                    }
                    else
                    {
                        DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Successfully merged. Carried thing is now {carriedThing.LabelCap}.");
                    }

                    if (actor.Map.reservationManager.ReservedBy(thingToPickUp, actor, curJob))
                    {
                        actor.Map.reservationManager.Release(thingToPickUp, actor, curJob);
                    }
                }
                else
                {
                    // --- “拿起”分支 ---
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"{actor.LabelShort} is taking {thingToPickUp.LabelCap} to CARRY.");

                    int plannedCount = curJob.count;
                    int countToPickUp = Mathf.Min(plannedCount, thingToPickUp.stackCount);
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Planned: {plannedCount}, On ground: {thingToPickUp.stackCount}. Will take: {countToPickUp}.");

                    if (countToPickUp <= 0)
                    {
                        DebugLogger.LogMessage(LogCategory.Toils, () => "-> Toil ABORTED: Calculated count to pick up is zero.");
                        return;
                    }

                    if (actor.Map.reservationManager.ReservedBy(thingToPickUp, actor, curJob))
                    {
                        actor.Map.reservationManager.Release(thingToPickUp, actor, curJob);
                    }

                    // 先分离，再尝试拿起，失败则丢弃
                    Thing itemToCarry = thingToPickUp.SplitOff(countToPickUp);

                    if (actor.carryTracker.TryStartCarry(itemToCarry))
                    {
                        // 只有在成功拿起后，才添加到 haulState
                        haulState.AddHauledThing(actor.carryTracker.CarriedThing);
                        DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Successfully carried {actor.carryTracker.CarriedThing.LabelCap}. HauledThings count: {haulState.HauledThings.Count}.");
                    }
                    else
                    {
                        // 如果 TryStartCarry 失败，将分离出的部分丢在地上，防止物品丢失
                        DebugLogger.LogMessage(LogCategory.Toils, () => $"  - TryStartCarry FAILED for {itemToCarry.LabelCap}. Dropping on ground.");
                        Log.Error($"[BulkLoad] Failed to carry {itemToCarry.LabelCap}. Placing back on ground.");
                        GenPlace.TryPlaceThing(itemToCarry, actor.Position, actor.Map, ThingPlaceMode.Near);
                    }
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}