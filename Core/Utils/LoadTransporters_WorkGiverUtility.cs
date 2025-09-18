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
            if (pawn.RaceProps.IsMechanoid && !PickUpAndHaul.Settings.AllowMechanoids) return false;
            if (CentralLoadManager.Instance == null) return false;

            CentralLoadManager.Instance.RegisterOrUpdateTask(groupLoadable);

            var parentThing = groupLoadable.GetParentThing();
            if (parentThing == null) return false;


            // 机会主义卸货是最高优先级的工作类型。
            if (BulkLoad_Utility.TryCreateUnloadFirstJob(pawn, groupLoadable, out _))
            {
                return true;
            }

            if (!CentralLoadManager.Instance.HasWork(groupLoadable))
            {
                return false;
            }

            if (!pawn.CanReach(parentThing, PathEndMode.Touch, Danger.Deadly)) return false;

            var availableToClaim = CentralLoadManager.Instance.GetAvailableToClaim(groupLoadable);
            if (!availableToClaim.Any()) return false;

            var allDemands = groupLoadable.GetTransferables();
            if (allDemands == null) return false;

            // 为了优化性能，我们按ThingDef对需求进行分组，避免重复的地图扫描。
            var demandsByDef = allDemands
                .Where(d => d.ThingDef != null && d.CountToTransfer > 0 && availableToClaim.ContainsKey(d.ThingDef))
                .GroupBy(d => d.ThingDef);

            foreach (var group in demandsByDef)
            {
                var neededDef = group.Key;

                IEnumerable<Thing> initialSourceList;

                // NOTE: 这是一个API兼容性修复。查询打包物品(MinifiedThing)必须使用特殊的方法。
                if (neededDef == ThingDefOf.MinifiedThing)
                {
                    initialSourceList = pawn.Map.listerThings.ThingsMatching(ThingRequest.ForGroup(ThingRequestGroup.MinifiedThing));
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

                if (!availableSourcesOnMap.Any()) continue; 

                foreach (var demand in group)
                {
                    if (demand.AnyThing is Pawn p && !NeedsToBeCarried(p))
                    {
                        // 如果这个Pawn会自己走路，就跳过，不为它分配“搬运”任务
                        continue;
                    }

                    if (FindBestSourceFor(pawn, demand, availableSourcesOnMap) != null)
                    {
                        return true;
                    }
                }
            }

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
            job = null;
            if (pawn.RaceProps.IsMechanoid && !PickUpAndHaul.Settings.AllowMechanoids) return false;


            if (BulkLoad_Utility.TryCreateUnloadFirstJob(pawn, groupLoadable, out Job unloadJob))
            {
                job = unloadJob;
                return true;
            }

            var constraints = CentralLoadManager.Instance.GetAvailableToClaim(groupLoadable);

            var plan = LoadingPlanner.TryCreateHaulPlan(pawn, groupLoadable, constraints);
            if (plan != null)
            {
                JobDef correctJobDef = JobDefRegistry.GetJobDefFor(groupLoadable.GetParentThing().def);
                if (correctJobDef == null || correctJobDef == JobDefOf.Wait)
                {
                    // 如果找不到匹配的 JobDef，安全失败
                    job = JobMaker.MakeJob(JobDefOf.Wait, 2, true);
                    return true;
                }
                job = JobMaker.MakeJob(correctJobDef);

                job.targetB = new LocalTargetInfo(groupLoadable.GetParentThing());
                job.targetQueueA = plan.Targets;
                job.countQueue = plan.Counts;
                return true;
            }

            job = JobMaker.MakeJob(JobDefOf.Wait, 2, true);
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