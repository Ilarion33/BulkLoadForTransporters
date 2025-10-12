// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Utils/WorkGiver_Utility.cs
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.HarmonyPatches.DeliverConstruction;
using PickUpAndHaul;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group; 

namespace BulkLoadForTransporters.Core.Utils
{
    /// <summary>
    /// A utility class containing the core logic for WorkGivers to decide upon and create bulk loading jobs.
    /// It is decoupled from specific Thing types (like CompTransporter or MapPortal) and operates via the IManagedLoadable interface.
    /// </summary>
    public static class WorkGiver_Utility
    {
        // --- 全局决策缓存 ---
        private static int _lastDecisionFrame = -1;
        private static Pawn _lastDecisionPawn;
        private static Dictionary<int, bool> _groupEvaluationCache;

        /// <summary>
        /// 为一个新的决策周期（同一个Pawn在同一个Tick内的所有WorkGiver调用）初始化或重置缓存。
        /// </summary>
        private static void BeginDecisionCycle(Pawn pawn)
        {
            if (Time.frameCount != _lastDecisionFrame || pawn != _lastDecisionPawn)
            {
                _groupEvaluationCache = new Dictionary<int, bool>();
                _lastDecisionFrame = Time.frameCount;
                _lastDecisionPawn = pawn;
            }
        }


        /// <summary>
        /// Performs a low-cost, preliminary check to see if any potential bulk loading work exists for a given pawn and target.
        /// </summary>
        /// <param name="pawn">The pawn considering the job.</param>
        /// <param name="groupLoadable">The loading task, abstracted via the IManagedLoadable interface.</param>
        /// <returns>True if there is a possibility of work, otherwise false.</returns>
        public static bool HasPotentialBulkWork(Pawn pawn, IManagedLoadable groupLoadable)
        {
            BeginDecisionCycle(pawn);

            // 被征召的 Pawn 不应该执行任何自动的批量装载工作。
            if (pawn.Drafted) return false;
            
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

            CentralLoadManager.Instance.RegisterOrUpdateTask(groupLoadable);

            // 机会主义卸货是最高优先级的工作类型。
            if (TryCreateUnloadFirstJob(pawn, groupLoadable, out _))
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> YES: Opportunistic unload job can be created.");
                return true;
            }

            // --- 缓存命中！直接使用结果，跳过昂贵的查询 ---
            int groupID = groupLoadable.GetUniqueLoadID();
            if (_groupEvaluationCache.TryGetValue(groupID, out bool cachedHasWork))
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"[CACHE HIT] Group {groupID} has potential pickup work: {cachedHasWork}.");
                return cachedHasWork;
            }

            // --- 缓存未命中，执行昂贵的检查逻辑 ---
            DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"[CACHE MISS] Evaluating pickup work for Group {groupID}.");
            
            if (!CentralLoadManager.Instance.HasWork(groupLoadable, pawn))
            {
                
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: CentralLoadManager reports no available items to claim for this task.");
                _groupEvaluationCache[groupID] = false;
                return false;
            }

            if (!pawn.CanReach(parentThing, PathEndMode.Touch, Danger.Deadly))
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"-> NO: Pawn cannot reach the target at {parentThing.Position}.");
                return false;
            }

            var availableToClaim = CentralLoadManager.Instance.GetAvailableToClaim(groupLoadable, pawn);
            if (!availableToClaim.Any())
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Double-checked with Manager, available to claim is now empty.");
                _groupEvaluationCache[groupID] = false;
                return false;
            }

            var allDemands = groupLoadable.GetTransferables();
            if (allDemands == null) return false;

            // 为了优化性能，我们按ThingDef对需求进行分组，避免重复的地图扫描。
            var demandsByDef = allDemands
                .Where(d => d.ThingDef != null && d.CountToTransfer > 0 && availableToClaim.ContainsKey(d.ThingDef))
                .GroupBy(d => d.ThingDef);

            bool isAbstract = groupLoadable.HandlesAbstractDemands;

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


                // --- 统一的、高性能的过滤逻辑 ---
                // 这个HashSet用于快速查找需求清单上明确指定的物品实例。
                var explicitlyDemandedThings = new HashSet<Thing>(group.SelectMany(d => d.things));

                var availableSourcesOnMap = initialSourceList
                    .Where(thing => {
                        // 条件 B (VIP 通道 #1): 物品是任务清单上明确指定的实例吗？
                        bool isExplicitlyDemanded = explicitlyDemandedThings.Contains(thing);

                        // 条件 A: 物品是“殖民地资产”吗？
                        bool isColonyAsset = Global_Utility.IsValidColonyAsset(thing, pawn);

                        if (!thing.IsForbidden(pawn) && (isExplicitlyDemanded || isAbstract || isColonyAsset))
                        {
                            // 只有当物品符合任一高优先级条件时，我们才进行昂贵的可达性检查。
                            return HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, false);
                        }

                        return false;
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
                    if (demand.AnyThing is Pawn p && !Global_Utility.NeedsToBeCarried(p))
                    {
                        // 如果这个Pawn会自己走路，就跳过，不为它分配“搬运”任务
                        continue;
                    }

                    if (TryFindAnyAvailableSourceFor(pawn, demand, availableSourcesOnMap) != null)
                    {
                        DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"-> YES: Found a valid source for demand '{demand.AnyThing.LabelCap}' of type {neededDef.defName}.");
                        _groupEvaluationCache[groupID] = true;
                        return true;
                    }
                }
            }

            DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Exhausted all demands and found no matchable sources on the map.");
            _groupEvaluationCache[groupID] = false;
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


            if (TryCreateUnloadFirstJob(pawn, groupLoadable, out Job unloadJob))
            {
                job = unloadJob;

                // 在Lambda表达式中使用 out 参数 'job' 的本地副本 'finalUnloadJob'
                Job finalUnloadJob = job;
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"-> YES: Created an opportunistic UNLOAD job with {finalUnloadJob.targetQueueA.Count} targets.");

                CentralLoadManager.Instance.ClaimItems(pawn, job, groupLoadable); //Toil_ReplanJob
                return true;
            }

            var constraints = CentralLoadManager.Instance.GetAvailableToClaim(groupLoadable, pawn);
            DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"  - No unload job. Proceeding to planning. Constraints from Manager: {constraints.Count} defs.");

            var plan = LoadingPlanner.TryCreateHaulPlan(pawn, groupLoadable, constraints);
            if (plan != null)
            {
                JobDef correctJobDef = JobDefRegistry.GetJobDefFor(groupLoadable.GetParentThing().def);
                if (correctJobDef == null || correctJobDef == JobDefOf.Wait)
                {
                    // 如果找不到匹配的 JobDef，安全失败
                    job = JobMaker.MakeJob(JobDefOf.Wait, 2, true);
                    DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"-> NO (Wait Job): Planner succeeded, but no matching JobDef was found in the registry for '{groupLoadable.GetParentThing().def.defName}'.");
                    return true;
                }
                job = JobMaker.MakeJob(correctJobDef);

                job.targetB = new LocalTargetInfo(groupLoadable.GetParentThing());
                job.targetQueueA = plan.Targets;
                job.countQueue = plan.Counts;
                // 在Lambda表达式中使用 out 参数 'job' 的本地副本 'finalLoadJob'
                Job finalLoadJob = job;
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"-> YES: Planner created a LOAD job with {finalLoadJob.targetQueueA.Count} targets.");

                CentralLoadManager.Instance.ClaimItems(pawn, job, groupLoadable); //Toil_ReplanJob
                return true;
            }

            job = JobMaker.MakeJob(JobDefOf.Wait, 2, true);
            DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO (Wait Job): Planner returned no valid plan.");
            return true;
        }

        /// <summary>
        /// Finds the best available Thing from a list of sources to satisfy a specific transferable demand.
        /// </summary>
        private static Thing TryFindAnyAvailableSourceFor(Pawn pawn, TransferableOneWay demand, IEnumerable<Thing> availableSources)
        {
            if (!demand.HasAnyThing) return null;

            bool needsExactMatch = !Global_Utility.IsFungible(demand.AnyThing);

            if (needsExactMatch)
            {
                var idealSource = availableSources.FirstOrDefault(s => demand.things.Contains(s));
                if (idealSource != null)
                {
                    return idealSource;
                }

                return availableSources.FirstOrDefault(s => Global_Utility.FindBestMatchFor(s, new List<TransferableOneWay> { demand }) != null);
            }
            else
            {
                return availableSources.FirstOrDefault();
            }
        }


        /// <summary>
        /// Checks if the pawn has anything in their Pick Up And Haul inventory that is needed by the target.
        /// </summary>
        public static bool PawnHasNeededPuahItems(Pawn pawn, ILoadable loadable)
        {
            var puahComp = pawn.TryGetComp<CompHauledToInventory>();
            return JobDriver_Utility.HasAnyNeededItems(puahComp?.GetHashSet(), loadable);
        }


        // 这是一个内部的Job构建器，负责为“仅卸货”模式创建Job。
        // 它可以通过IManagedLoadable接口为任何兼容的目标服务。
        private static Job CreateUnloadJobForTarget(Pawn pawn, IManagedLoadable loadable, IEnumerable<Thing> itemsInBag)
        {
            Job job = JobMaker.MakeJob(JobDefRegistry.GetJobDefFor(loadable.GetParentThing().def));
            job.targetB = new LocalTargetInfo(loadable.GetParentThing());
            job.haulOpportunisticDuplicates = true;

            job.targetQueueA = new List<LocalTargetInfo>();
            job.countQueue = new List<int>();

            var needs = loadable.GetTransferables();
            if (needs == null) return null;

            foreach (var item in itemsInBag)
            {
                if (item != null && !item.Destroyed && Global_Utility.FindBestMatchFor(item, needs) != null)
                {
                    job.targetQueueA.Add(new LocalTargetInfo(item));
                    job.countQueue.Add(item.stackCount);
                }
            }
            return job;
        }



        /// <summary>
        /// Tries to create an "unload only" job by opportunistically scanning for the **best nearby** target.
        /// </summary>
        public static bool TryCreateUnloadFirstJob(Pawn pawn, IManagedLoadable loadable, out Job job)
        {
            job = null;
            BeginDecisionCycle(pawn);

            DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"Should an opportunistic UNLOAD job be created for {pawn.LabelShort}?");
            if (!PawnHasNeededPuahItems(pawn, loadable))
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Pawn is not carrying anything needed by the initial target.");
                return false;
            }

            var itemsInBag = pawn.TryGetComp<PickUpAndHaul.CompHauledToInventory>().GetHashSet();
            DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"  - Pawn has {itemsInBag.Count} items in PUAH inventory. Scanning for targets...");

            // 扫描半径设置
            var settings = LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>();
            float scanRadiusSquared = settings.opportunityScanRadius * settings.opportunityScanRadius;

            // 整合“感知过滤器”的范围限制
            if (PerceptionFilterController.IsActive)
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "  - Perception Filter is ACTIVE. Using the smaller of the two radii.");
                scanRadiusSquared = Mathf.Min(scanRadiusSquared, PerceptionFilterController.RadiusSquared);
            }

            // 统一扫描所有潜在的可装载目标类型
            var allPossibleTargets = new List<Thing>();
            foreach (var scanner in Extensibility_Utility.OpportunisticTargetScanners)
            {
                allPossibleTargets.AddRange(scanner(pawn));
            }
            // 去重，以防万一
            allPossibleTargets = allPossibleTargets.Distinct().ToList();
            DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"  - Found {allPossibleTargets.Count} potential targets on map. Starting evaluation...");

            foreach (var targetThing in allPossibleTargets
            .Where(t =>
                !t.IsForbidden(pawn) &&
                t.Position.DistanceToSquared(pawn.Position) <= scanRadiusSquared
            )
            .OrderBy(t => pawn.Position.DistanceToSquared(t.Position)))
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"    - Evaluating target: '{targetThing.LabelCap}' at {targetThing.Position}...");

                // 首先，检查小人是否有资格处理这个工地
                if (targetThing is IConstructible constructible && !JobDriver_Utility.CanPawnWorkOnSite(pawn, constructible))
                {
                    DebugLogger.LogMessage(LogCategory.WorkGiver, () => "      - Check FAILED: Pawn cannot work on this site (e.g., needs construction skill).");
                    continue;
                }

                // --- 阶段一：廉价的“个体”初筛 ---
                IManagedLoadable individualAdapter = null;
                foreach (var factory in Extensibility_Utility.AdapterFactories)
                {
                    individualAdapter = factory(targetThing, pawn);
                    if (individualAdapter != null) break;
                }

                if (individualAdapter == null) continue;

                // 检查这个“个体”是否需要我们背包里的任何东西
                if (itemsInBag.Any(item => item != null && !item.Destroyed && Global_Utility.FindBestMatchFor(item, individualAdapter.GetTransferables()) != null))
                {
                    DebugLogger.LogMessage(LogCategory.WorkGiver, () => "      - Micro-check PASSED: Target individually needs items from pawn's inventory.");
                    // --- 阶段二：昂贵的“群体 + Manager”复核 ---
                    // 将视野提升到群体级别
                    var groupAdapter = Extensibility_Utility.CreateGroupAdapterFor(targetThing, pawn);
                    if (groupAdapter == null)
                    {
                        DebugLogger.LogMessage(LogCategory.WorkGiver, () => "      - Macro-check FAILED: Could not create a group adapter for this target.");
                        continue;
                    }


                    int groupID = groupAdapter.GetUniqueLoadID();
                    bool groupHasWork;

                    if (_groupEvaluationCache.TryGetValue(groupID, out groupHasWork))
                    {
                        // 缓存命中！直接使用结果，跳过昂贵的查询。
                        DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"      - Macro-check CACHE HIT for Group {groupID}. Result: {groupHasWork}.");
                    }
                    else
                    {
                        // 缓存未命中，执行原始的“宏观检查”
                        var availableToClaim = CentralLoadManager.Instance.GetAvailableToClaim(groupAdapter, pawn);
                        groupHasWork = itemsInBag.Any(item => item != null && !item.Destroyed && availableToClaim.TryGetValue(item.def, out int needed) && needed > 0);

                        // 将结果存入缓存以备后用
                        _groupEvaluationCache[groupID] = groupHasWork;
                        DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"      - Macro-check CACHE MISS for Group {groupID}. Result: {groupHasWork}. Storing in cache.");
                    }
                                        
                    // 最终决策：这个群体是否还真的需要我们背包里的东西？
                    if (groupHasWork)
                    {
                        DebugLogger.LogMessage(LogCategory.WorkGiver, () => "      - Macro-check PASSED: Group's net needs match items in pawn's inventory.");
                        // 找到了！立即创建 Job 并返回
                        job = CreateUnloadJobForTarget(pawn, groupAdapter, itemsInBag);
                        if (job != null && job.targetQueueA.Any())
                        {
                            // --- 日志 #2: 成功找到并创建 Job ---
                            Job finalJob = job; // for lambda capture
                            DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"-> YES: Created an opportunistic UNLOAD job for target '{groupAdapter.GetParentThing().LabelCap}' with {finalJob.targetQueueA.Count} items to unload.");
                            return true;
                        }
                    }
                    else
                    {
                        DebugLogger.LogMessage(LogCategory.WorkGiver, () => "      - Macro-check FAILED: No match after considering Manager's claims.");
                    }
                }
            }

            // 遍历完所有目标，都没有找到完全有效的机会
            DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Exhausted all nearby targets and found no valid unload opportunity.");
            job = null;
            return false;
        }



        /// <summary>
        /// Tries to create an "unload only" job for a **specifically designated** target,
        /// typically one that the player has right-clicked.
        /// </summary>
        public static bool TryCreateDirectedUnloadJob(Pawn pawn, IManagedLoadable designatedTarget, out Job job)
        {
            job = null;
            if (!PawnHasNeededPuahItems(pawn, designatedTarget))
            {
                return false;
            }


            var itemsInBag = pawn.TryGetComp<CompHauledToInventory>().GetHashSet();
            job = CreateUnloadJobForTarget(pawn, designatedTarget, itemsInBag);

            return job != null && job.targetQueueA.Any();
        }

        /// <summary>
        /// The universal logic for chaining to the next optimal construction job.
        /// Can be called from an EndSession Toil or a Harmony patch after a cleanup job.
        /// It performs a highly optimized, perception-filtered scan to find the best next target.
        /// </summary>
        /// <param name="pawn">The pawn looking for the next job.</param>
        public static void TryChainToNextConstructionJob(Pawn pawn)
        {
            DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"{pawn.LabelShort} is attempting to chain to the next construction job.");

            // --- 激活并配置“感知过滤器” ---
            var settings = LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>();
            int GridSize = settings.constructionGroupingGridSize;

            if (settings.constructionChainScanRadius <= 1f)
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "  - Job chaining for construction is disabled in settings. Ending chain.");
                return;
            }

            PerceptionFilterController.IsActive = true;
            PerceptionFilterController.Center = pawn.Position;
            PerceptionFilterController.RadiusSquared = settings.constructionChainScanRadius * settings.constructionChainScanRadius;
            OptimisticHaulingController.IsInBulkPlanningPhase = true;

            try
            {
                var nearbyConstructionSites = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                    .Concat(pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
                    .Where(t =>
                        t.Faction == pawn.Faction &&
                        !t.IsForbidden(pawn) &&
                        !(t is Blueprint_Install) &&
                        !Compatibility_Utility.IsReplaceStuffFrame(t)) // For ReplaceStuff
                    .OfType<IConstructible>()
                    .Where(c => JobDriver_Utility.CanPawnWorkOnSite(pawn, c));
                    //.ToList();

                if (!nearbyConstructionSites.Any())
                {
                    DebugLogger.LogMessage(LogCategory.WorkGiver, () => "  - No nearby, workable construction sites found within perception filter. Ending chain.");
                    return;
                }

                // --- “海选即决选”逻辑 ---
                Thing bestTargetSoFar = null;
                float minDistanceSq = float.MaxValue;
                var evaluatedGroupIDs = new HashSet<int>();

                foreach (var site in nearbyConstructionSites)
                {
                    var siteThing = site as Thing;
                    int gridID = Gen.HashCombineInt(siteThing.Position.x / GridSize, siteThing.Position.z / GridSize);
                    if (evaluatedGroupIDs.Contains(gridID)) continue;

                    evaluatedGroupIDs.Add(gridID);

                    var groupAdapter = new ConstructionGroupAdapter(site, pawn);
                    //CentralLoadManager.Instance.RegisterOrUpdateTask(groupAdapter);

                    if (HasPotentialBulkWork(pawn, groupAdapter))
                    {
                        foreach (var member in groupAdapter.GetJobTargets())
                        {
                            var memberConstructible = member as IConstructible;
                            if (memberConstructible != null && memberConstructible.TotalMaterialCost().Any(cost => memberConstructible.ThingCountNeeded(cost.thingDef) > 0))
                            {
                                float distanceSq = pawn.Position.DistanceToSquared(member.Position);
                                if (distanceSq < minDistanceSq)
                                {
                                    minDistanceSq = distanceSq;
                                    bestTargetSoFar = member;
                                }
                            }
                        }
                    }
                }

                if (bestTargetSoFar != null)
                {
                    var finalAdapter = new ConstructionGroupAdapter(bestTargetSoFar as IConstructible, pawn);
                    if (TryGiveBulkJob(pawn, finalAdapter, out Job nextJob) && nextJob.def != JobDefOf.Wait)
                    {
                        DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"    - Successfully created job '{nextJob.def.defName}'. Attempting to chain...");
                        pawn.jobs.jobQueue.EnqueueFirst(nextJob);
                    }
                }
                else
                {
                    CentralLoadManager.Instance.ReleaseClaimsForPawn(pawn);
                    DebugLogger.LogMessage(LogCategory.WorkGiver, () => "  - No nearby groups with available work found. Ending chain.");
                }
            }
            finally
            {
                PerceptionFilterController.IsActive = false;
                OptimisticHaulingController.IsInBulkPlanningPhase = false;
            }
        }
    }
}