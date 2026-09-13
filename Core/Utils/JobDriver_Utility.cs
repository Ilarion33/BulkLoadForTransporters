// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Utils/JobDriver_Utility.cs
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Jobs;
using PickUpAndHaul;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Core.Utils
{
    public static class JobDriver_Utility
    {        
        
        /// <summary>
        /// Validates if the pawn's carried items are still needed by a single, non-grouped target (e.g., a Portal).
        /// This version does not perform complex redirection.
        /// </summary>
        public static bool ValidateSingleTarget(JobDriver_BulkLoadBase driver, IManagedLoadable loadable)
        {
            var pawn = driver.pawn;
            var job = driver.job;

            var carriedThings = driver.HauledThings
                .Where(t => t != null && !t.Destroyed && (pawn.inventory.innerContainer.Contains(t) || pawn.carryTracker.CarriedThing == t))
                .ToList();

            if (!carriedThings.Any())
            {
                return true;
            }

            var individualNeeds = loadable.GetTransferables();
            if (individualNeeds == null)
            {
                return false;
            }

            bool isTargetStillValid = carriedThings.Any(carried => Global_Utility.FindBestMatchFor(carried, individualNeeds) != null);

            return isTargetStillValid;
        }

        /// <summary>
        /// A powerful validation tool for grouped targets (e.g., Transporters).
        /// If the current target no longer needs the carried items, it automatically finds the next valid
        /// target within the group and redirects the pawn's path.
        /// </summary>
        public static bool ValidateAndRedirectCurrentTarget(JobDriver_LoadTransportersInBulk driver)
        {
            var pawn = driver.pawn;
            var job = driver.job;

            var carriedThing = pawn.carryTracker.CarriedThing;
            Thing priorityThing = null;

            // 如果手上拿着一个Pawn，它就是我们的最高优先级目标
            if (carriedThing != null && carriedThing is Pawn)
            {
                priorityThing = carriedThing;
            }

            List<Thing> thingsToValidate;
            if (priorityThing != null)
            {
                thingsToValidate = new List<Thing> { priorityThing };
            }
            else
            {
                thingsToValidate = driver.HauledThings
                    .Where(t => t != null && !t.Destroyed && (pawn.inventory.innerContainer.Contains(t) || t == carriedThing))
                    .ToList();
            }

            if (!thingsToValidate.Any())
            {
                return true; 
            }

            var currentTargetThing = job.targetB.Thing;
            if (currentTargetThing == null || currentTargetThing.Destroyed) return false;

            var currentTransporter = currentTargetThing.TryGetComp<CompTransporter>();
            if (currentTransporter == null) return false;

            var individualNeeds = currentTransporter.leftToLoad;
            bool isCurrentTargetValid = individualNeeds != null &&
                                      thingsToValidate.Any(thing => Global_Utility.FindBestMatchFor(thing, individualNeeds) != null);

            if (isCurrentTargetValid)
            {
                return true; 
            }

            // 如果当前目标无效，则在整个组内“寻址”
            var groupTransporters = currentTransporter.TransportersInGroup(pawn.Map);
            if (groupTransporters == null) return false;

            var bestTarget = groupTransporters
                .Where(tr => tr.parent != currentTargetThing) // 排除当前目标
                .Select(tr => new { Transporter = tr, Needs = tr.leftToLoad }) // 缓存 leftToLoad 避免重复获取
                .Where(x => x.Needs != null && thingsToValidate.Any(thing => Global_Utility.FindBestMatchFor(thing, x.Needs) != null)) // 检查需求
                .OrderBy(x => pawn.Position.DistanceToSquared(x.Transporter.parent.Position)) // 按距离排序
                .Select(x => x.Transporter.parent) // 选择最终的 Thing 目标
                .FirstOrDefault(t => pawn.CanReach(t, PathEndMode.Touch, Danger.Deadly)); // 找到第一个可达的

            if (bestTarget != null)
            {
                job.targetB = new LocalTargetInfo(bestTarget);
                // 如果小人正在移动，则为其重新规划路径
                if (pawn.pather != null && pawn.pather.Moving)
                {
                    pawn.pather.StartPath(job.targetB, PathEndMode.Touch);
                }
                return true;
            }


            return false;
        }

        // 这是一个内部辅助方法，用来检查一个物品列表（thingsToCheck）和
        // 一个需求列表（loadable）之间是否存在任何交集。
        public static bool HasAnyNeededItems(IEnumerable<Thing> thingsToCheck, ILoadable loadable)
        {
            if (thingsToCheck == null || !thingsToCheck.Any())
            {
                return false;
            }

            var transferables = loadable.GetTransferables();
            if (transferables == null)
            {
                return false;
            }

            foreach (var thing in thingsToCheck)
            {
                if (thing != null && !thing.Destroyed && Global_Utility.FindBestMatchFor(thing, transferables) != null)
                {
                    return true;
                }
            }
            return false;
        }


        /// <summary>
        /// Checks if at least one of the items currently being hauled for our job is still needed by the target.
        /// </summary>
        public static bool IsCarryingAnythingNeeded(Pawn pawn, ILoadable loadable)
        {
            if (!(pawn.jobs.curDriver is IBulkHaulState haulState))
            {
                return false;
            }
            return HasAnyNeededItems(haulState.HauledThings, loadable);
        }

        

        /// <summary>
        /// Creates a delegate (Action) that will be executed at the end of a job (in a FinishAction).
        /// It's responsible for the final synchronization of our internal item state with Pick Up And Haul's system.
        /// </summary>
        public static Action<JobCondition> CreatePuahReconciliationAction(Pawn pawn, IBulkHaulState haulState, List<Thing> originalPuahItems)
        {
            return (jobCondition) =>
            {
                var puahComp = pawn.TryGetComp<CompHauledToInventory>();
                if (puahComp == null) return;
                var puahSet = puahComp.GetHashSet();

                foreach (var originalThing in originalPuahItems)
                {
                    puahSet.Remove(originalThing);
                }

                foreach (var surplusThing in haulState.SurplusThings)
                {
                    if (surplusThing != null && !surplusThing.Destroyed)
                    {
                        puahComp.RegisterHauledItem(surplusThing);
                    }
                }

                foreach (var hauledThing in haulState.HauledThings)
                {
                    if (hauledThing != null && !hauledThing.Destroyed)
                    {
                        puahComp.RegisterHauledItem(hauledThing);
                    }
                }
            };
        }

        

        /// <summary>
        /// Resolves the precise number of an item still needed during an unload session.
        /// This is crucial for preventing over-loading if the player alters the manifest mid-job.
        /// It checks against both the original transferable and the session's remaining needs budget.
        /// </summary>
        /// <param name="thingToUnload">The specific Thing instance being considered for unloading.</param>
        /// <param name="allTransferables">The complete list of transferables for the entire loading task.</param>
        /// <param name="unloadSessionNeeds">A dictionary representing the remaining "budget" of needs for the current pawn's unload trip.</param>
        /// <returns>The precise number of items to deposit.</returns>
        public static int ResolveNeededAmountForUnload(Thing thingToUnload, List<TransferableOneWay> allTransferables, Dictionary<ThingDef, int> unloadSessionNeeds)
        {
            if (allTransferables == null || thingToUnload == null || unloadSessionNeeds == null) return 0;

            // 首先，检查本趟卸货的“预算”中，是否还需要这种类型的物品。
            if (!unloadSessionNeeds.TryGetValue(thingToUnload.def, out int totalNeeded) || totalNeeded <= 0) return 0;

            // 其次，找到与当前物品最匹配的那个原始需求条目。
            var bestMatch = Global_Utility.FindBestMatchFor(thingToUnload, allTransferables);

            // 返回两者中的最小值，确保既不超过总需求，也不超过本趟预算。
            if (bestMatch != null)
            {
                return Mathf.Min(bestMatch.CountToTransfer, totalNeeded);
            }

            return 0;
        }


    }
}
