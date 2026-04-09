// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_UnloadCarriers/Toil_SelectNextItemFromContainer.cs
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_UnloadCarriers
{
    /// <summary>
    /// A utility class for creating the item selection Toil.
    /// </summary>
    public static class Toil_SelectNextItemFromContainer
    {
        /// <summary>
        /// Creates a Toil that selects the next best item to unload from a container.
        /// </summary>
        public static Toil Create(TargetIndex containerInd, IBulkHaulState haulState, Toil jumpTargetIfEmpty)
        {
            Toil toil = ToilMaker.MakeToil("SelectNextItemFromContainer");
            toil.initAction = () =>
            {
                Pawn pawn = toil.actor;
                Job job = pawn.CurJob;
                Thing containerThing = job.GetTarget(containerInd).Thing;

                var containerOwner = containerThing?.TryGetInnerInteractableThingOwner();
                if (containerOwner == null || !containerOwner.Any)
                {
                    pawn.jobs.curDriver.JumpToToil(jumpTargetIfEmpty);
                    return;
                }

                Thing finalThingToTake = null;
                int finalCountToTake = 0;
                bool takeToCarryTracker = false;

                // NOTE: 这是此Toil的核心决策逻辑。它遵循一个清晰的优先级顺序。
                // --- 1. 优先寻找可入包且能拿得动的物品 ---
                float availableMass = MassUtility.FreeSpace(pawn);
                if (availableMass > 1E-05f)
                {
                    foreach (var thing in containerOwner.Where(t => !Global_Utility.IsUnbackpackable(t)))
                    {
                        float massPerItem = thing.GetStatValue(StatDefOf.Mass);
                        int countAffordable = (massPerItem > 1E-05f) ? Mathf.FloorToInt(availableMass / massPerItem) : thing.stackCount;

                        if (countAffordable > 0)
                        {
                            finalThingToTake = thing;
                            finalCountToTake = Mathf.Min(thing.stackCount, countAffordable);
                            takeToCarryTracker = false;
                            break;
                        }
                    }
                }

                // 如果容器里只剩下我们刚刚选中的那一件物品，就改为拿到手上。
                if (finalThingToTake != null && containerOwner.Count == 1 && containerOwner.Contains(finalThingToTake))
                {
                    takeToCarryTracker = true;
                    finalCountToTake = finalThingToTake.stackCount;
                }

                // --- 2. 如果背包规划失败，则尝试拿一件到手上作为后备 ---
                // NOTE: 这个后备逻辑解决了“逻辑死区”问题，
                // 确保在背包几乎已满时，能正确过渡到手持规划。
                if (finalThingToTake == null && pawn.carryTracker.CarriedThing == null)
                {
                    finalThingToTake = containerOwner.FirstOrDefault();

                    if (finalThingToTake != null)
                    {
                        finalCountToTake = finalThingToTake.stackCount;
                        takeToCarryTracker = true;
                    }
                }

                if (finalThingToTake == null)
                {
                    pawn.jobs.curDriver.JumpToToil(jumpTargetIfEmpty);
                    return;
                }

                job.SetTarget(TargetIndex.C, finalThingToTake);
                job.count = finalCountToTake;
                job.haulOpportunisticDuplicates = takeToCarryTracker;
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}