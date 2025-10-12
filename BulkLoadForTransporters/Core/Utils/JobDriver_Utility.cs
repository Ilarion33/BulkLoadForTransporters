// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Utils/JobDriver_Utility.cs
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.HarmonyPatches.DeliverConstruction;
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

        /// <summary>
        /// The comprehensive, all-in-one validation and redirection tool for construction jobs.
        /// It handles both Blueprint-to-Frame conversion and redirection to other needy sites within the group.
        /// </summary>
        public static bool ValidateAndRedirectConstructionTarget(JobDriver_DeliverConstructionInBulk driver)
        {
            var pawn = driver.pawn;
            var job = driver.job;
            var groupAdapter = driver.GroupAdapter;

            if (groupAdapter == null) return false;

            var targetThing = job.GetTarget(TargetIndex.B).Thing;

            bool currentTargetIsForbidden = targetThing != null && targetThing.Spawned && targetThing.IsForbidden(pawn);

            // --- Step 1: Handle Blueprint -> Frame state transition ---
            if (targetThing == null || targetThing.Destroyed)
            {
                // This logic is a safe replication of JobDriver_HaulToContainer.TryReplaceWithFrame
                Building edificeAtTarget = targetThing.Position.GetEdifice(pawn.Map);
                if (edificeAtTarget != null)
                {
                    var blueprint = targetThing as Blueprint_Build;
                    if (blueprint != null)
                    {
                        var frame = edificeAtTarget as Frame;
                        if (frame != null && frame.BuildDef == blueprint.BuildDef)
                        {
                            // Success! Redirect the job's target to the new Frame.
                            job.SetTarget(TargetIndex.B, frame);
                            targetThing = frame; // Update our local reference for the next steps.
                        }
                    }
                }
            }

            // If, after attempting redirection, the target is still invalid, the job must fail.
            if (targetThing == null || !targetThing.Spawned)
            {
                return false;
            }

            // --- Step 2: Validate if the current target still needs the carried items ---
            var carriedThings = driver.HauledThings
                .Where(t => t != null && !t.Destroyed && (pawn.inventory.innerContainer.Contains(t) || pawn.carryTracker.CarriedThing == t))
                .ToList();

            if (!carriedThings.Any()) return true; // Nothing to validate against.

            var currentTargetConstructible = targetThing as IConstructible;
            if (currentTargetConstructible == null) return false; // Should not happen if target is Frame/Blueprint

            if (!currentTargetIsForbidden && carriedThings.Any(carried => currentTargetConstructible.ThingCountNeeded(carried.def) > 0))
            {
                return true; // Current target is still valid and needs our stuff.
            }

            // --- Step 3: Find a new, valid target within the construction group ---
            var allGroupMembers = groupAdapter.GetJobTargets();
            var bestTarget = allGroupMembers
                .Where(member => member != targetThing && !member.IsForbidden(pawn)) // 排除当前目标和禁区
                .Select(member => new { Thing = member, Constructible = member as IConstructible }) // 缓存类型转换
                .Where(x => x.Constructible != null && carriedThings.Any(carried => x.Constructible.ThingCountNeeded(carried.def) > 0)) // 检查需求
                .OrderBy(x => pawn.Position.DistanceToSquared(x.Thing.Position)) // 按距离排序
                .Select(x => x.Thing) // 选择最终的 Thing 目标
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


            // No valid alternative was found in the entire group.
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


        // --- “信使”邮戳 ID ---
        public const int CleanupJobLoadID = 886611;

        /// <summary>
        /// A precise, static replica of WorkGiver_ConstructDeliverResources.ShouldRemoveExistingFloorFirst.
        /// Checks if a floor blueprint requires removing an existing floor before construction.
        /// </summary>
        private static bool ShouldRemoveExistingFloorFirst_Internal(Pawn pawn, Blueprint blue)
        {
            return blue.def.entityDefToBuild is TerrainDef && pawn.Map.terrainGrid.CanRemoveTopLayerAt(blue.Position);
        }

        /// <summary>
        /// A precise, static replica of WorkGiver_ConstructDeliverResources.RemoveExistingFloorJob.
        /// Creates a job to remove the floor beneath a blueprint if necessary.
        /// </summary>
        private static Job CreateRemoveFloorJob_Internal(Pawn pawn, Blueprint blue)
        {
            if (!ShouldRemoveExistingFloorFirst_Internal(pawn, blue))
            {
                return null;
            }
            if (!pawn.CanReserve(blue.Position, 1, -1, ReservationLayerDefOf.Floor, false))
            {
                return null;
            }
            // NOTE: The original checks the WorkGiver's def. We check the pawn's enabled work types directly,
            // which is functionally equivalent and more robust for our context.
            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction))
            {
                return null;
            }
            Job job = JobMaker.MakeJob(JobDefOf.RemoveFloor, blue.Position);
            job.ignoreDesignations = true;
            return job;
        }

        /// <summary>
        /// The unified, high-level interface for all pre-delivery site clearing tasks.
        /// It checks for and creates jobs for physical blockers, floor removal, and material replacement.
        /// </summary>
        /// <param name="pawn">The pawn performing the check.</param>
        /// <param name="target">The construction site (Blueprint or Frame).</param>
        /// <param name="forced">Whether the job was player-forced.</param>
        /// <param name="cleanupJob">The resulting cleanup job, if one is needed.</param>
        /// <returns>True if a cleanup job was created, otherwise false.</returns>
        public static bool TryCreateClearSiteJob(Pawn pawn, Thing target, bool forced, out Job cleanupJob)
        {
            bool wasDeceptionActive = OptimisticHaulingController.IsInBulkPlanningPhase;
            OptimisticHaulingController.IsInBulkPlanningPhase = false;
            try
            {
                // Priority 1: Physical blockers (handles rocks, deconstruction, material replacement)
                Thing blockingThing = GenConstruct.FirstBlockingThing(target, pawn);
                if (blockingThing != null)
                {
                    cleanupJob = GenConstruct.HandleBlockingThingJob(target, pawn, forced);
                    if (cleanupJob != null)
                    {
                        // --- 关键变更：为 Job 打上“信使”标记 ---
                        cleanupJob.loadID = CleanupJobLoadID;
                        return true;
                    }
                }

                // Priority 2: Floor removal
                if (target is Blueprint blueprint)
                {
                    cleanupJob = CreateRemoveFloorJob_Internal(pawn, blueprint);
                    if (cleanupJob != null)
                    {
                        // --- 关键变更：为 Job 打上“信使”标记 ---
                        cleanupJob.loadID = CleanupJobLoadID;
                        return true;
                    }
                }

                cleanupJob = null;
                return false;
            }
            finally
            {
                // --- 恢复之前的欺骗状态 ---
                OptimisticHaulingController.IsInBulkPlanningPhase = wasDeceptionActive;
            }
        }

        /// <summary>
        /// The definitive, global standard for checking if a pawn is qualified to work on a construction site.
        /// It checks for all pre-construction requirements like floor removal or deconstruction.
        /// </summary>
        /// <param name="pawn">The pawn to check.</param>
        /// <param name="site">The construction site (Blueprint or Frame).</param>
        /// <returns>True if the pawn can handle all pre-requisite tasks for this site.</returns>
        public static bool CanPawnWorkOnSite(Pawn pawn, IConstructible site)
        {
            var siteThing = site as Thing;
            if (siteThing == null) return false;

            // 1. 记录当前的欺骗状态
            bool wasDeceptionActive = OptimisticHaulingController.IsInBulkPlanningPhase;
            // 2. 强制关闭欺骗，以获取真实信息
            OptimisticHaulingController.IsInBulkPlanningPhase = false;

            try
            {
                if (Compatibility_Utility.IsReplaceStuffLoaded && site is Blueprint)
                {
                    // 遍历蓝图下的所有格子
                    foreach (var cell in siteThing.OccupiedRect())
                    {
                        // 检查格子上是否有任何“可开采的岩石”
                        var thingsInCell = cell.GetThingList(pawn.Map);
                        for (int i = 0; i < thingsInCell.Count; i++)
                        {
                            if (Compatibility_Utility.IsMineableRock_Replica(thingsInCell[i].def))
                            {
                                // 发现“岩上蓝图”，不处理。
                                return false;
                            }
                        }
                    }
                }

                bool needsConstructionSkill = false;

                // --- 门槛 A: 检查是否需要换地板 ---
                if (site is Blueprint blueprintForFloor && blueprintForFloor.def.entityDefToBuild is TerrainDef)
                {
                    if (pawn.Map.terrainGrid.CanRemoveTopLayerAt(siteThing.Position))
                    {
                        needsConstructionSkill = true;
                    }
                }

                // --- 门槛 B: 检查是否需要拆除建筑 (材料替换等) ---
                if (!needsConstructionSkill)
                {
                    Thing blockingThing = GenConstruct.FirstBlockingThing(siteThing, pawn);
                    if (blockingThing != null)
                    {
                        if (blockingThing is Pawn)
                        {
                            return false; // 工地上站着人，直接拒绝
                        }

                        Building blockingBuilding = blockingThing as Building;
                        if (blockingBuilding != null && blockingBuilding.DeconstructibleBy(pawn.Faction))
                        {
                            needsConstructionSkill = true;
                        }
                    }
                }

                // 需要建造技能，但小人是“纯搬运工”，拒绝
                if (needsConstructionSkill && !pawn.workSettings.WorkIsActive(WorkTypeDefOf.Construction))
                {
                    return false;
                }

                return true;
            }
            finally
            {
                // --- 3. 无论发生什么，都恢复之前的欺骗状态 ---
                OptimisticHaulingController.IsInBulkPlanningPhase = wasDeceptionActive;
            }
        }
    }
}