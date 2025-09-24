// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadTransporters/Toil_TakeToInventory.cs
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_LoadTransporters
{
    /// <summary>
    /// A static class containing the factory method for the "take to inventory" Toil.
    /// </summary>
    public static class Toil_TakeToInventory
    {
        /// <summary>
        /// Creates a Toil that makes the pawn pick up a target Thing and place it in their inventory.
        /// </summary>
        /// <param name="index">The TargetIndex where the Thing to be picked up is stored.</param>
        /// <param name="haulState">The JobDriver's state interface for tracking hauled items.</param>
        /// <param name="loadable">The loading task interface, used to check remaining needs.</param>
        /// <returns>A configured Toil ready to be used in a JobDriver.</returns>
        public static Toil Create(TargetIndex index, IBulkHaulState haulState, ILoadable loadable)
        {
            Toil toil = ToilMaker.MakeToil("TakeToInventory");
            toil.initAction = () =>
            {
                Pawn actor = toil.actor;
                Job curJob = actor.CurJob;
                DebugLogger.LogMessage(LogCategory.Toils, () => $"{actor.LabelShort} is executing TakeToInventory.");
                Thing thingToPickUp = curJob.GetTarget(index).Thing;
                if (thingToPickUp == null || thingToPickUp.Destroyed)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => "-> Toil ABORTED: Target thing is null or destroyed.");
                    return;
                }

                // 在释放预定之前，先检查我们是否真的拥有它。
                if (actor.Map.reservationManager.ReservedBy(thingToPickUp, actor, curJob))
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Reservation released for {thingToPickUp.LabelCap}.");
                    actor.Map.reservationManager.Release(thingToPickUp, actor, curJob);
                }

                // 综合考虑任务总需求和已携带数量，计算“净需求”。
                int stillNeededFromMap = GetCurrentNeededAmountFor(loadable, thingToPickUp.def, haulState);
                int plannedCount = curJob.count;
                // 最终拾取量是“规划量”、“物品堆叠量”和“净需求量”三者中的最小值。
                int countToPickUp = Mathf.Min(plannedCount, thingToPickUp.stackCount, stillNeededFromMap);
                DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Calculation: Planned={plannedCount}, OnGround={thingToPickUp.stackCount}, StillNeeded={stillNeededFromMap} -> Initial countToPickUp={countToPickUp}.");

                // 进行最终的物理负重检查。
                float massPerItem = thingToPickUp.GetStatValue(StatDefOf.Mass);
                if (massPerItem > 0)
                {
                    float availableMass = MassUtility.FreeSpace(actor);
                    int amountAffordableByMass = (availableMass > 0) ? Mathf.FloorToInt(availableMass / massPerItem) : 0;
                    countToPickUp = Mathf.Min(countToPickUp, amountAffordableByMass);
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Mass Check: AvailableMass={availableMass:F2}kg, AffordableCount={amountAffordableByMass} -> Final countToPickUp={countToPickUp}.");
                }
                if (countToPickUp <= 0)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => "-> Toil ABORTED: Final countToPickUp is 0.");
                    return;
                }


                Thing splitThing = thingToPickUp.SplitOff(countToPickUp);
                DebugLogger.LogMessage(LogCategory.Toils, () => $"  - SplitOff {countToPickUp} of {thingToPickUp.def.defName}.");

                // 尝试与背包中已有的同类物品合并。
                Thing targetStack = haulState.HauledThings.FirstOrDefault(t => t.CanStackWith(splitThing));
                if (targetStack != null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Found existing stack in haulState. Merging {splitThing.LabelCap} into {targetStack.LabelCap}.");
                    targetStack.TryAbsorbStack(splitThing, true);
                    if (!splitThing.Destroyed)
                    {
                        if (actor.inventory.GetDirectlyHeldThings().TryAdd(splitThing, false))
                        {
                            haulState.AddHauledThing(splitThing);
                            DebugLogger.LogMessage(LogCategory.Toils, () => $"    - Merge overflow successful. Added new stack {splitThing.LabelCap} to inventory and haulState.");
                        }
                        else
                        {
                            Log.Error($"[BulkLoad] Failed to add overflow stack {splitThing.LabelCap} to inventory. Placing back on ground.");
                            GenPlace.TryPlaceThing(splitThing, thingToPickUp.Position, actor.Map, ThingPlaceMode.Near);
                        }
                    }
                    else
                    {
                        DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Merge successful. Original stack count is now {targetStack.stackCount}.");
                    }
                }
                else
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - No existing stack found. Adding {splitThing.LabelCap} as a new entry.");
                    // 如果没有可合并的，就作为新堆叠添加入背包。
                    if (actor.inventory.GetDirectlyHeldThings().TryAdd(splitThing, false))
                    {
                        haulState.AddHauledThing(splitThing);
                        DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Added new stack {splitThing.LabelCap} to inventory and haulState.");
                    }
                    else
                    {
                        Log.Error($"[BulkLoad] Failed to add new stack {splitThing.LabelCap} to inventory. Placing back on ground.");
                        GenPlace.TryPlaceThing(splitThing, thingToPickUp.Position, actor.Map, ThingPlaceMode.Near);
                    }
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }

        /// <summary>
        /// Calculates the net amount of a given ThingDef that still needs to be picked up from the map.
        /// It subtracts the amount already hauled by the pawn from the total required amount.
        /// </summary>
        internal static int GetCurrentNeededAmountFor(ILoadable loadable, ThingDef thingDef, IBulkHaulState haulState)
        {
            var transferables = loadable.GetTransferables();
            if (transferables == null) return 0;

            int totalNeeded = transferables.Where(t => t.ThingDef == thingDef).Sum(t => t.CountToTransfer);
            if (totalNeeded <= 0) return 0;

            int alreadyHauled = 0;
            if (haulState != null && haulState.HauledThings != null)
            {
                alreadyHauled = haulState.HauledThings
                                         .Where(t => t != null && !t.Destroyed && t.def == thingDef)
                                         .Sum(t => t.stackCount);
            }

            return Mathf.Max(0, totalNeeded - alreadyHauled);
        }
    }
}