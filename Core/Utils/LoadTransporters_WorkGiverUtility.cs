// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Utils/LoadTransporters_WorkGiverUtility.cs
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using PickUpAndHaul;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group; 

namespace BulkLoadForTransporters.Core.Utils
{
    /// <summary>
    /// A utility class containing the core logic for WorkGivers to decide upon and create bulk loading jobs.
    /// It is decoupled from specific Thing types (like CompTransporter or MapPortal) and operates via the IManagedLoadable interface.
    /// </summary>
    public static class LoadTransporters_WorkGiverUtility
    {
        /// <summary>
        /// Performs a low-cost, preliminary check to see if any potential bulk loading work exists for a given pawn and target.
        /// </summary>
        /// <param name="pawn">The pawn considering the job.</param>
        /// <param name="groupLoadable">The loading task, abstracted via the IManagedLoadable interface.</param>
        /// <returns>True if there is a possibility of work, otherwise false.</returns>
        public static bool HasPotentialBulkWork(Pawn pawn, IManagedLoadable groupLoadable)
        {
            var parentThingForLog = groupLoadable.GetParentThing();
            DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"Does {pawn.LabelShort} have any potential bulk work for '{parentThingForLog?.LabelCap}' at {parentThingForLog?.Position}?");
            if (!groupLoadable.GetTransferables().Any())
            {
                return false;
            }

            if (pawn.RaceProps.IsMechanoid && !PickUpAndHaul.Settings.AllowMechanoids)
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Pawn is a mechanoid and PUAH settings disallow it.");
                return false;
            }

            if (CentralLoadManager.Instance == null)
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: CentralLoadManager is not available.");
                return false;
            }


            var parentThing = groupLoadable.GetParentThing();
            if (parentThing == null)
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Loadable's parent thing is null.");
                return false;
            }


            // 机会主义卸货是最高优先级的工作类型。
            if (BulkLoad_Utility.TryCreateUnloadFirstJob(pawn, groupLoadable, out _))
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> YES: Opportunistic unload job can be created.");
                return true;
            }

            CentralLoadManager.Instance.RegisterOrUpdateTask(groupLoadable);

            if (!CentralLoadManager.Instance.HasWork(groupLoadable))
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: CentralLoadManager reports no available items to claim for this task.");
                return false;
            }

            if (!pawn.CanReach(parentThing, PathEndMode.Touch, Danger.Deadly))
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"-> NO: Pawn cannot reach the target at {parentThing.Position}.");
                return false;
            }

            var availableToClaim = CentralLoadManager.Instance.GetAvailableToClaim(groupLoadable);
            if (!availableToClaim.Any())
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Double-checked with Manager, available to claim is now empty.");
                return false;
            }

            var allDemands = groupLoadable.GetTransferables();
            if (allDemands == null) return false;

            // 为了优化性能，我们按ThingDef对需求进行分组，避免重复的地图扫描。
            var demandsByDef = allDemands
                .Where(d => d.ThingDef != null && d.CountToTransfer > 0 && availableToClaim.ContainsKey(d.ThingDef))
                .GroupBy(d => d.ThingDef);

            foreach (var group in demandsByDef)
            {
                var neededDef = group.Key;
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"  - Checking for sources of {neededDef.defName}...");

                IEnumerable<Thing> initialSourceList;

                // NOTE: 这是一个API兼容性修复。查询打包物品(MinifiedThing)必须使用特殊的方法。
                if (neededDef == ThingDefOf.MinifiedThing)
                {
                    initialSourceList = pawn.Map.listerThings.ThingsMatching(ThingRequest.ForGroup(ThingRequestGroup.MinifiedThing));
                    DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"    - Using ThingsMatching for MinifiedThing.");
                }
                else
                {
                    initialSourceList = pawn.Map.listerThings.ThingsOfDef(neededDef);
                }

                Area effectiveArea = null;
                if (pawn.Map.IsPlayerHome)
                {
                    effectiveArea = pawn.Map.areaManager.Home;
                }
                System.Func<IntVec3, bool> InAllowedArea = (IntVec3 pos) => effectiveArea == null || effectiveArea[pos];

                var availableSourcesOnMap = initialSourceList
                    .Where(thing => {
                        // 通用检查 (对所有Thing都适用)
                        if (!thing.Spawned || thing.IsForbidden(pawn) || !pawn.CanReserveAndReach(thing, PathEndMode.ClosestTouch, Danger.Deadly))
                        {
                            return false;
                        }

                        // 区域限制检查
                        if (!(thing is Pawn) && !InAllowedArea(thing.Position))
                        {
                            return false;
                        }
                         
                        return true;
                    });

                // HACK: 这是“书架难题”的解决方案。如果在地板上找不到书，我们就扩大搜索范围，检查书架内部。
                var bookCategory = ThingCategoryDef.Named("Books"); 
                bool isSearchingForBooks = neededDef.thingCategories != null && neededDef.thingCategories.Contains(bookCategory);

                if (!availableSourcesOnMap.Any() && isSearchingForBooks)
                {
                    DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"    - No sources of {neededDef.defName} found on ground. Expanding search to bookshelves.");
                    var booksInShelves = new List<Thing>();
                    var bookshelves = pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Bookcase>();
                    foreach (var shelf in bookshelves)
                    {
                        if (shelf.IsForbidden(pawn) || !pawn.CanReach(shelf, PathEndMode.Touch, Danger.Deadly)) continue;

                        var container = shelf.GetDirectlyHeldThings();
                        if (container != null)
                        {
                            foreach (var book in container.Where(b => b.def == neededDef))
                            {
                                booksInShelves.Add(book);
                            }
                        }
                    }

                    if (booksInShelves.Any())
                    {
                        availableSourcesOnMap = booksInShelves;
                    }
                }

                if (!availableSourcesOnMap.Any())
                {
                    DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"    - No available sources found for {neededDef.defName} after all checks. Continuing to next def.");
                    continue;
                }

                foreach (var demand in group)
                {
                    if (demand.AnyThing is Pawn p && !NeedsToBeCarried(p))
                    {
                        // 如果这个Pawn会自己走路，就跳过，不为它分配“搬运”任务
                        continue;
                    }

                    if (FindBestSourceFor(pawn, demand, availableSourcesOnMap) != null)
                    {
                        DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"-> YES: Found a valid source for demand '{demand.AnyThing.LabelCap}' of type {neededDef.defName}.");
                        return true;
                    }
                }
            }

            DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Exhausted all demands and found no matchable sources on the map.");
            return false;
        }

        /// <summary>
        /// Performs the full, higher-cost planning process to create a concrete bulk loading job.
        /// </summary>
        /// <param name="pawn">The pawn who will perform the job.</param>
        /// <param name="groupLoadable">The loading task, abstracted via the IManagedLoadable interface.</param>
        /// <param name="job">The resulting job, or a WaitJob if no plan could be made.</param>
        /// <returns>Always returns true, indicating the request has been handled (even if by creating a WaitJob).</returns>
        public static bool TryGiveBulkJob(Pawn pawn, IManagedLoadable groupLoadable, out Job job)
        {
            var parentThingForLog = groupLoadable.GetParentThing();
            DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"Should a bulk job be given to {pawn.LabelShort} for '{parentThingForLog?.LabelCap}' at {parentThingForLog?.Position}?");
            job = null;
            if (pawn.RaceProps.IsMechanoid && !PickUpAndHaul.Settings.AllowMechanoids)
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO (return false): Pawn is a mechanoid and PUAH settings disallow it.");
                return false;
            }


            if (BulkLoad_Utility.TryCreateUnloadFirstJob(pawn, groupLoadable, out Job unloadJob))
            {
                job = unloadJob;

                // 在Lambda表达式中使用 out 参数 'job' 的本地副本 'finalUnloadJob'
                Job finalUnloadJob = job;
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"-> YES: Created an opportunistic UNLOAD job with {finalUnloadJob.targetQueueA.Count} targets.");

                return true;
            }

            var constraints = CentralLoadManager.Instance.GetAvailableToClaim(groupLoadable);
            DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"  - No unload job. Proceeding to planning. Constraints from Manager: {constraints.Count} defs.");

            var plan = LoadingPlanner.TryCreateHaulPlan(pawn, groupLoadable, constraints);
            if (plan != null)
            {
                JobDef correctJobDef = JobDefRegistry.GetJobDefFor(groupLoadable.GetParentThing().def);
                if (correctJobDef == null || correctJobDef == JobDefOf.Wait)
                {
                    // 如果找不到匹配的 JobDef，安全失败
                    job = JobMaker.MakeJob(JobDefOf.Wait, 2, true);
                    DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"-> YES (Wait Job): Planner succeeded, but no matching JobDef was found in the registry for '{groupLoadable.GetParentThing().def.defName}'.");
                    return true;
                }
                job = JobMaker.MakeJob(correctJobDef);

                job.targetB = new LocalTargetInfo(groupLoadable.GetParentThing());
                job.targetQueueA = plan.Targets;
                job.countQueue = plan.Counts;
                // 在Lambda表达式中使用 out 参数 'job' 的本地副本 'finalLoadJob'
                Job finalLoadJob = job;
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"-> YES: Planner created a LOAD job with {finalLoadJob.targetQueueA.Count} targets.");

                return true;
            }

            job = JobMaker.MakeJob(JobDefOf.Wait, 2, true);
            DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> YES (Wait Job): Planner returned no valid plan.");
            return true;
        }

        /// <summary>
        /// Finds the best available Thing from a list of sources to satisfy a specific transferable demand.
        /// </summary>
        private static Thing FindBestSourceFor(Pawn pawn, TransferableOneWay demand, IEnumerable<Thing> availableSources)
        {
            if (!demand.HasAnyThing) return null;

            bool needsExactMatch = !BulkLoad_Utility.IsFungible(demand.AnyThing);

            if (needsExactMatch)
            {
                var idealSource = availableSources.FirstOrDefault(s => demand.things.Contains(s));
                if (idealSource != null)
                {
                    return idealSource;
                }

                return availableSources.FirstOrDefault(s => BulkLoad_Utility.FindBestMatchFor(s, new List<TransferableOneWay> { demand }) != null);
            }
            else
            {
                return availableSources.FirstOrDefault();
            }
        }

        /// <summary>
        /// The definitive logic gate to determine if a Pawn is cargo or an autonomous agent for a loading task.
        /// </summary>
        public static bool NeedsToBeCarried(Pawn p)
        {
            if (p.Downed || p.Dead)
            {
                return true;
            }

            // AI接到了“自主登船”命令的单位，会自己走路。
            if (p.mindState?.duty?.def == DutyDefOf.LoadAndEnterTransporters)
            {
                return false;
            }


            // 被系统视为“殖民者”的单位（包括玩家可控的机械体）
            if (p.IsColonist || p.IsColonyMech)
            {
                return false;
            }

            return true;
        }

    }
}