// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/LoadingPlanner.cs
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using PickUpAndHaul;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Core
{
    /// <summary>
    /// The strategic planner for creating multi-stop bulk hauling jobs.
    /// It solves a variation of the knapsack and traveling salesman problems to generate an efficient pickup sequence for a pawn.
    /// </summary>
    public static class LoadingPlanner
    {
        /// <summary>
        /// Represents a finalized hauling plan, containing the sequence of targets and the corresponding item counts to pick up.
        /// </summary>
        public class HaulPlan
        {
            public List<LocalTargetInfo> Targets;
            public List<int> Counts;
        }
        private class HaulCandidate
        {
            public DemandAnalysis Analysis;
            public TransferableOneWay Demand;
            public Thing Source;
            public float HeuristicDistanceSq;
            //public float TruePathCost; 
        }
        private class DemandAnalysis
        {
            public TransferableOneWay OriginalDemand;
            public List<Thing> IdealSources;
            public List<Thing> AlternativeSources;
            //public List<Thing> UnbackpackableSources;
            public int IdealNeeded;
            public int AlternativeNeeded;
        }

        // 私有内部类，用于在规划过程中创建一个“沙盒”版本的Transferable，避免修改原始的任务清单实例。
        private class TransferableOneWay_BulkLoad : TransferableOneWay
        {
            public int originalCountToTransfer;
            public TransferableOneWay_BulkLoad(TransferableOneWay original)
            {
                if (original.things != null) this.things = new List<Thing>(original.things);
                else this.things = new List<Thing>();
                this.originalCountToTransfer = original.CountToTransfer;
                this.AdjustTo(original.CountToTransfer);
            }
            public override int GetMaximumToTransfer() => this.originalCountToTransfer;
        }

        /// <summary>
        /// The main entry point for creating a haul plan.
        /// </summary>
        /// <param name="pawn">The pawn that will execute the plan.</param>
        /// <param name="loadable">An interface representing the loading destination (e.g., a transporter group or a portal).</param>
        /// <param name="constraints">A dictionary of concurrency limits from the CentralLoadManager.</param>
        /// <returns>A HaulPlan object if a valid plan can be made; otherwise, null.</returns>
        public static HaulPlan TryCreateHaulPlan(Pawn pawn, ILoadable loadable, Dictionary<ThingDef, int> constraints)
        {
            DebugLogger.LogMessage(LogCategory.Planner, () => {
                var sb = new StringBuilder();

                // 1. 尝试将 ILoadable 转换为更具体的 IManagedLoadable
                var managedLoadable = loadable as IManagedLoadable;
                string targetLabel = "Unknown Target";
                string targetPos = "N/A";

                // 2. 只有在转换成功时，才调用 GetParentThing
                if (managedLoadable != null)
                {
                    var parentThing = managedLoadable.GetParentThing();
                    targetLabel = parentThing?.LabelCap ?? "Null Parent";
                    targetPos = parentThing?.Position.ToString() ?? "N/A";
                }

                sb.AppendLine($"Should a haul plan be created for {pawn.LabelShort} for '{targetLabel}' at {targetPos}?");
                sb.AppendLine($"  - Pawn capacity: {MassUtility.Capacity(pawn):F2}kg. Free space: {MassUtility.FreeSpace(pawn):F2}kg.");
                sb.AppendLine($"  - Target capacity: {loadable.GetMassCapacity():F2}kg. Usage: {loadable.GetMassUsage():F2}kg.");
                return sb.ToString();
            });

            if (constraints == null) constraints = new Dictionary<ThingDef, int>();

            #region Setup
            var puahComp = pawn.TryGetComp<CompHauledToInventory>();
            if (puahComp == null || pawn.CurJobDef == PickUpAndHaulJobDefOf.UnloadYourHauledInventory) return null;

            Map map = loadable.GetMap();
            if (map == null) return null;

            List<TransferableOneWay> originalTransferables = loadable.GetTransferables();
            if (originalTransferables == null || !originalTransferables.Any(t => t != null && t.CountToTransfer > 0)) return null;

            var sandboxTransferables = originalTransferables
                .Where(o => o != null)
                .Select(o => new TransferableOneWay_BulkLoad(o))
                .Cast<TransferableOneWay>()
                .ToList();
            DebugLogger.LogMessage(LogCategory.Planner, () => $"  - Initial analysis: {sandboxTransferables.Count} transferable types to consider.");

            // 检查需求清单中是否包含了正在执行此任务的 pawn 自己。
            sandboxTransferables.RemoveAll(tr => tr.things.Contains(pawn));

            #region Capacity Calculations

            float pawnMaxHaulMass = MassUtility.FreeSpace(pawn);
            bool backpackIsFullOrOverweight = pawnMaxHaulMass <= 1E-05f; // 使用一个小的容差

            // 如果背包没满，才计算背包的装载上限
            float finalMaxMass = 0f;
            if (!backpackIsFullOrOverweight)
            {
                float groupMassCapacity = loadable.GetMassCapacity();
                float groupMassUsage = loadable.GetMassUsage();
                finalMaxMass = Mathf.Min(pawnMaxHaulMass, groupMassCapacity - groupMassUsage);
            }
            DebugLogger.LogMessage(LogCategory.Planner, () => $"  - Capacity calculated. Backpack is {(backpackIsFullOrOverweight ? "full/overweight" : "not full")}. Final plan mass limit: {finalMaxMass:F2}kg.");
            #endregion
            #endregion

            // 确定我们需要寻找的所有物品类型
            var allNeededDefs = sandboxTransferables
                                  .Where(tr => tr.ThingDef != null && tr.CountToTransfer > 0 && tr.HasAnyThing)
                                  .Select(tr => tr.ThingDef)
                                  .Where(def => def != null)
                                  .ToHashSet();

            if (!allNeededDefs.Any())
            {
                DebugLogger.LogMessage(LogCategory.Planner, () => "-> NO PLAN: No valid defs needed after filtering.");
                return null;
            }

            // 构建一次性的、预先过滤的供应品索引
            var supplyIndex = new Dictionary<ThingDef, List<Thing>>();

            Area effectiveArea = null;
            if (pawn.Map.IsPlayerHome)
            {
                effectiveArea = pawn.Map.areaManager.Home;
            }
            System.Func<IntVec3, bool> InAllowedArea = (IntVec3 pos) => effectiveArea == null || effectiveArea[pos];

            // --- 2a. 高效扫描物品 (Things that are not Pawns) ---
            foreach (var thing in map.listerThings.ThingsMatching(ThingRequest.ForGroup(ThingRequestGroup.HaulableEver)))
            {
                if (thing == null) continue;

                if (!InAllowedArea(thing.Position)) continue;
                // 跳过活着的Pawn，它们将在下一步被专门处理
                if (thing is Pawn && !(thing is Corpse)) continue;

                if (allNeededDefs.Contains(thing.def))
                {
                    if (thing.Spawned && !thing.IsForbidden(pawn) && pawn.CanReserveAndReach(thing, PathEndMode.ClosestTouch, Danger.Deadly))
                    {
                        if (!supplyIndex.TryGetValue(thing.def, out var list))
                        {
                            list = new List<Thing>();
                            supplyIndex[thing.def] = list;
                        }
                        list.Add(thing);
                    }
                }
            }

            // --- 2b. 高效扫描活着的Pawn ---
            foreach (var livePawn in map.mapPawns.AllPawnsSpawned)
            {
                if (livePawn == null) continue;

                if (allNeededDefs.Contains(livePawn.def))
                {
                    if (!livePawn.IsForbidden(pawn) && pawn.CanReserveAndReach(livePawn, PathEndMode.ClosestTouch, Danger.Deadly))
                    {
                        if (!supplyIndex.TryGetValue(livePawn.def, out var list))
                        {
                            list = new List<Thing>();
                            supplyIndex[livePawn.def] = list;
                        }
                        // 防止重复添加（虽然不太可能发生）
                        if (!list.Contains(livePawn))
                        {
                            list.Add(livePawn);
                        }
                    }
                }
            }

            // --- 2c. 扫描所有书架，将其中的书也加入供应品索引 ---
            var bookCategory = ThingCategoryDef.Named("Books");
            bool needsBooks = allNeededDefs.Any(def => def.thingCategories != null && def.thingCategories.Contains(bookCategory));

            if (needsBooks)
            {
                DebugLogger.LogMessage(LogCategory.Planner, () => "  - Task requires books. Scanning bookshelves...");
                // 只有在确实需要书的情况下，才扫描书架
                var bookshelves = map.listerBuildings.AllBuildingsColonistOfClass<Building_Bookcase>();
                foreach (var shelf in bookshelves)
                {
                    if (shelf.IsForbidden(pawn)) continue;

                    var container = shelf.GetDirectlyHeldThings();
                    if (container != null)
                    {
                        foreach (var book in container)
                        {
                            if (book == null) continue;

                            if (allNeededDefs.Contains(book.def))
                            {
                                if (pawn.CanReach(shelf, PathEndMode.Touch, Danger.Deadly))
                                {
                                    if (!supplyIndex.TryGetValue(book.def, out var list))
                                    {
                                        list = new List<Thing>();
                                        supplyIndex[book.def] = list;
                                    }
                                    list.Add(book);
                                }
                            }
                        }
                    }
                }
            }

            // 使用有序列表代替无序字典来存储规划结果
            var shoppingBasket = new List<KeyValuePair<Thing, int>>();
            float currentHaulMass = 0f;

            ProcessDemands_Optimized(pawn, sandboxTransferables, supplyIndex, shoppingBasket, ref currentHaulMass, finalMaxMass, constraints, backpackIsFullOrOverweight);
            DebugLogger.LogMessage(LogCategory.Planner, () => $"  - ProcessDemands complete. Shopping basket has {shoppingBasket.Count} raw entries. Final mass: {currentHaulMass:F2}kg.");

            // HACK: ProcessDemands_Optimized 在其循环中可能会为同一个物品源（例如，一堆钢铁）
            // 生成多个拾取条目（例如，先拿20个，再拿30个）。
            // 这会导致 JobDriver 中出现冗余的目标。这个聚合步骤将多个连续的、来自同一源的拾取计划合并为一个，例如 `(Steel x20), (Steel x30)` -> `(Steel x50)`。
            if (shoppingBasket.Any())
            {
                var originalCount = shoppingBasket.Count;
                var finalShoppingBasket = new List<KeyValuePair<Thing, int>>();
                finalShoppingBasket.Add(shoppingBasket.First());

                for (int i = 1; i < shoppingBasket.Count; i++)
                {
                    var currentItem = shoppingBasket[i];
                    var lastItemInFinal = finalShoppingBasket.Last();

                    if (currentItem.Key == lastItemInFinal.Key)
                    {
                        finalShoppingBasket[finalShoppingBasket.Count - 1] = new KeyValuePair<Thing, int>(
                            lastItemInFinal.Key,
                            lastItemInFinal.Value + currentItem.Value
                        );
                    }
                    else
                    {
                        finalShoppingBasket.Add(currentItem);
                    }
                }
                shoppingBasket = finalShoppingBasket;
                if (originalCount != shoppingBasket.Count)
                {
                    DebugLogger.LogMessage(LogCategory.Planner, () => $"  - Shopping basket aggregated from {originalCount} to {shoppingBasket.Count} entries.");
                }
            }

            if (!shoppingBasket.Any())
            {
                DebugLogger.LogMessage(LogCategory.Planner, () => "-> NO PLAN: Shopping basket is empty after processing.");
                return null;
            }

            var thingsToHaul = new List<LocalTargetInfo>();
            var countsToHaul = new List<int>();

            foreach (var item in shoppingBasket
                .Where(kvp => kvp.Key != null && !kvp.Key.Destroyed)
                //.OrderBy(kvp => pawn.Position.DistanceToSquared(kvp.Key.Position))
                )
            {
                thingsToHaul.Add(new LocalTargetInfo(item.Key));
                countsToHaul.Add(item.Value);
            }

            if (!thingsToHaul.Any())
            {
                DebugLogger.LogMessage(LogCategory.Planner, () => "-> NO PLAN: No valid things to haul after final filtering.");
                return null;
            }
            DebugLogger.LogMessage(LogCategory.Planner, () => $"-> YES: Plan created successfully with {thingsToHaul.Count} distinct targets.");

            return new HaulPlan { Targets = thingsToHaul, Counts = countsToHaul };
        }

        // ProcessDemands_Optimized是一个私有辅助方法，是规划器的核心计算引擎。
        // 它被特意设计为不返回任何值，而是通过引用修改传入的 shoppingBasket 和 currentHaulMass
        private static void ProcessDemands_Optimized(
            Pawn pawn,
            List<TransferableOneWay> sandboxTransferables,
            Dictionary<ThingDef, List<Thing>> supplyIndex,
            List<KeyValuePair<Thing, int>> shoppingBasket,
            ref float currentHaulMass,
            float finalMaxMass,
            Dictionary<ThingDef, int> constraints,
            bool backpackIsFullOrOverweight)
        {
            DebugLogger.LogMessage(LogCategory.Planner, () => "--- Starting ProcessDemands_Optimized ---");

            var remainingStackCounts = new Dictionary<Thing, int>();
            foreach (var list in supplyIndex.Values)
                foreach (var thing in list)
                    remainingStackCounts[thing] = thing.stackCount;

            // HACK: 这个算法是对“旅行商问题”的一个贪婪近似解。
            // 它不在一开始就计算所有可能的路径，而是在每一步都做出局部最优选择：
            // “从我当前（或上一个物品）的位置出发，下一个要拿的东西去哪里最近？”
            // 这在大多数情况下能生成足够好的路径，同时避免了阶乘级的计算复杂度。
            var pathingOrigin = pawn.Position;
            bool wasBackpackFilled = false;

            // --- 阶段一: 需求预计算 (Pre-computation) ---
            var demandAnalyses = new List<DemandAnalysis>();
            var unbackpackableSourcesByDemand = new Dictionary<TransferableOneWay, List<Thing>>();

            DebugLogger.LogMessage(LogCategory.Planner, () => "  - Phase 1: Pre-computing demands...");
            foreach (var demand in sandboxTransferables)
            {
                // 它就是一个无效的需求，必须跳过以防止崩溃。
                if (demand.ThingDef == null) continue;

                if (demand.CountToTransfer <= 0) continue;

                var analysis = new DemandAnalysis { OriginalDemand = demand };
                analysis.IdealSources = new List<Thing>();
                analysis.AlternativeSources = new List<Thing>();
                unbackpackableSourcesByDemand[demand] = new List<Thing>();

                if (supplyIndex.TryGetValue(demand.ThingDef, out var sources) && sources != null)
                {
                    foreach (var source in sources)
                    {
                        // --- 1. 顶层过滤器: 该 source 是否应该被考虑 ---
                        if (source is Pawn p && !LoadTransporters_WorkGiverUtility.NeedsToBeCarried(p))
                        {
                            continue;
                        }

                        // --- 2. 身份鉴定: 该 source 是理想品、替代品，还是无关品? ---
                        bool isIdeal = false;
                        bool isAlternative = false;

                        if (demand.AnyThing is Pawn)
                        {
                            if (demand.things.Contains(source))
                            {
                                isIdeal = true;
                            }
                        }
                        else if (BulkLoad_Utility.IsFungible(demand.AnyThing))
                        {
                            isIdeal = true;
                        }
                        else
                        {
                            if (demand.things.Contains(source))
                            {
                                isIdeal = true;
                            }
                            else if (BulkLoad_Utility.FindBestMatchFor(source, new List<TransferableOneWay> { demand }) != null)
                            {
                                isAlternative = true;
                            }
                        }

                        // --- 3. 分配: 根据“身份”和“物理属性”将 source 放入正确的桶 ---
                        if (isIdeal)
                        {
                            if (BulkLoad_Utility.IsUnbackpackable(source))
                                unbackpackableSourcesByDemand[demand].Add(source);
                            else
                                analysis.IdealSources.Add(source);
                        }
                        else if (isAlternative)
                        {
                            if (!BulkLoad_Utility.IsUnbackpackable(source))
                            {
                                analysis.AlternativeSources.Add(source);
                            }
                        }
                    }
                }


                int allowedByManager = constraints.TryGetValue(demand.ThingDef, 0);
                if (allowedByManager <= 0)
                {
                    analysis.IdealNeeded = 0;
                    analysis.AlternativeNeeded = 0;
                }
                else
                {
                    int idealAvailable = analysis.IdealSources.Sum(s => remainingStackCounts.ContainsKey(s) ? remainingStackCounts[s] : 0);
                    analysis.IdealNeeded = Mathf.Min(demand.CountToTransfer, idealAvailable, allowedByManager);

                    // 替代品的需求，是总需求减去理想品能满足的部分后，再受manager约束
                    int remainingAllowed = allowedByManager - analysis.IdealNeeded;
                    analysis.AlternativeNeeded = Mathf.Min(demand.CountToTransfer - analysis.IdealNeeded, remainingAllowed);
                }

                demandAnalyses.Add(analysis);
            }
            DebugLogger.LogMessage(LogCategory.Planner, () => $"  - Phase 1 COMPLETE. Analyzed {demandAnalyses.Count} demands.");

            // --- 阶段 1.5: 一次性构建主候选池 ---
            var masterBackpackCandidatePool = new List<HaulCandidate>();
            foreach (var analysis in demandAnalyses)
            {
                // 将 analysis 对象直接附加到 candidate 上
                foreach (var source in analysis.IdealSources)
                    masterBackpackCandidatePool.Add(new HaulCandidate { Analysis = analysis, Source = source });

                foreach (var source in analysis.AlternativeSources)
                    masterBackpackCandidatePool.Add(new HaulCandidate { Analysis = analysis, Source = source });
            }
            DebugLogger.LogMessage(LogCategory.Planner, () => $"  - Phase 1.5 COMPLETE. Master backpack candidate pool created with {masterBackpackCandidatePool.Count} entries.");

            // --- 阶段二: 背包规划循环 ---
            int pathingBudgetUsed = 0;
            var settings = LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>();

            if (!backpackIsFullOrOverweight)
            {
                DebugLogger.LogMessage(LogCategory.Planner, () => "  - Phase 2: Starting backpack planning loop.");
                while (currentHaulMass < finalMaxMass)
                {
                    // 从预构建池中进行O(C)的高效线性筛选
                    var currentlyAvailableCandidates = masterBackpackCandidatePool.Where(c =>
                        (remainingStackCounts.ContainsKey(c.Source) && remainingStackCounts[c.Source] > 0) &&
                        (c.Analysis.IdealSources.Contains(c.Source)
                            ? c.Analysis.IdealNeeded > 0
                            : c.Analysis.AlternativeNeeded > 0)
                    ).ToList();

                    if (!currentlyAvailableCandidates.Any())
                    {
                        DebugLogger.LogMessage(LogCategory.Planner, () => "    - No more available candidates in the pool. Breaking backpack loop.");
                        break;
                    }
                    DebugLogger.LogMessage(LogCategory.Planner, () => $"    - Found {currentlyAvailableCandidates.Count} available candidates for this iteration.");

                    // NOTE: 这是“理想品优先”原则的体现。
                    // 规划器会优先寻找并满足玩家在清单中明确指定的那个物品实例（例如，保质期3天食品）。
                    // 只有在找不到理想品时，才会去寻找替代品。（例如，用保质期2天食品代替）
                    // 这是通过 FindBestChoiceFromCandidates 内部的排序和筛选实现的。
                    HaulCandidate bestChoice = FindBestChoiceFromCandidates(pawn, currentlyAvailableCandidates, pathingBudgetUsed, settings.pathfindingHeuristicCandidates, pathingOrigin);

                    if (bestChoice == null)
                    {
                        DebugLogger.LogMessage(LogCategory.Planner, () => "    - FindBestChoiceFromCandidates returned null. Breaking backpack loop.");
                        break;
                    }
                    DebugLogger.LogMessage(LogCategory.Planner, () => $"    - Best choice is: {bestChoice.Source.LabelCap} from {bestChoice.Source.Position}.");

                    // NOTE: 这是一个关键的性能与精度的权衡。
                    // 当 N > 1 时，这意味着我们正在使用“海选+决选”的混合模式。
                    // 我们只对距离最近的N个候选者进行昂贵的真实路径计算。
                    // 每次进行这种昂贵的计算，我们都消耗一次“预算”。
                    if (settings.pathfindingHeuristicCandidates > 1 && pathingBudgetUsed < settings.pathfindingHeuristicCandidates)
                    {
                        pathingBudgetUsed++;
                    }

                    var bestDemand = bestChoice.Analysis.OriginalDemand;
                    var bestSource = bestChoice.Source;
                    var relevantAnalysis = bestChoice.Analysis;

                    if (relevantAnalysis == null)
                    {
                        Log.Warning($"[BulkLoad] Planner inconsistency: Could not find analysis for a chosen demand. Skipping.");
                        continue;
                    }

                    int needed = (relevantAnalysis.IdealSources.Contains(bestSource))
                        ? relevantAnalysis.IdealNeeded
                        : relevantAnalysis.AlternativeNeeded;

                    int amountToTake = 0;
                    float massPerItem = bestSource.GetStatValue(StatDefOf.Mass);

                    // NOTE: 这是“背包已满”的最终物理检查。
                    // 无论需求还剩多少，如果根据质量计算，连一个都塞不进背包了，
                    // amountAffordable 就会是0，从而导致 amountToTake 为0。
                    int amountAffordable = (massPerItem > 0) ? Mathf.FloorToInt((finalMaxMass - currentHaulMass) / massPerItem) : int.MaxValue;
                    DebugLogger.LogMessage(LogCategory.Planner, () => $"      - Needed: {needed}, Available: {remainingStackCounts[bestSource]}, Manager: {constraints.TryGetValue(bestSource.def, 0)}, Affordable by mass: {amountAffordable}.");

                    if (amountAffordable > 0)
                    {

                        amountToTake = Mathf.Min(
                            remainingStackCounts[bestSource],
                            needed,
                            constraints.TryGetValue(bestSource.def, 0),
                            amountAffordable
                        );
                    }
                    DebugLogger.LogMessage(LogCategory.Planner, () => $"      - Final amountToTake: {amountToTake}.");


                    if (amountToTake <= 0)
                    {
                        wasBackpackFilled = true;
                        DebugLogger.LogMessage(LogCategory.Planner, () => "    - amountToTake is 0. Setting backpack as filled and breaking loop.");
                        break;
                    }

                    shoppingBasket.Add(new KeyValuePair<Thing, int>(bestSource, amountToTake));
                    currentHaulMass += amountToTake * massPerItem;
                    remainingStackCounts[bestSource] -= amountToTake;
                    constraints[bestSource.def] -= amountToTake;
                    pathingOrigin = bestSource.Position;
                    //if (usePrecisePathing) pathingBudgetUsed++;

                    //var relevantAnalysis = demandAnalyses.First(a => a.OriginalDemand == bestDemand);
                    if (relevantAnalysis.IdealSources.Contains(bestSource))
                        relevantAnalysis.IdealNeeded -= amountToTake;
                    else
                        relevantAnalysis.AlternativeNeeded -= amountToTake;

                    // 在更新完负重后，立刻检查是否已经达到或超过了背包容量。
                    // 如果是，就设置旗标并立即终止背包规划，以便进入手持规划。
                    if (currentHaulMass >= finalMaxMass)
                    {
                        wasBackpackFilled = true;
                        // 在Lambda表达式中使用 ref 参数 'currentHaulMass' 的本地副本 'finalMass'
                        float finalMass = currentHaulMass;
                        DebugLogger.LogMessage(LogCategory.Planner, () => $"    - currentHaulMass ({finalMass:F2}) reached finalMaxMass ({finalMaxMass:F2}). Breaking loop.");
                        break;
                    }
                }
            }
            else
            {
                DebugLogger.LogMessage(LogCategory.Planner, () => "  - Phase 2: Backpack planning loop SKIPPED because backpack was already full/overweight.");
            }

            // --- 阶段三: 手持规划 ---
            bool hasUnbackpackableThings = unbackpackableSourcesByDemand.Values.Any(l => l.Any(s => remainingStackCounts.ContainsKey(s) && remainingStackCounts[s] > 0));

            // NOTE: 手持规划的触发条件
            // 1. wasBackpackFilled: 背包满了，理应拿一个在手上。
            // 2. backpackIsFullOrOverweight: 因为出发时就超重，也应该尝试拿一个。
            // 3. hasUnbackpackableThings: 即使背包没满，但任务清单里有必须手持的物品（如动物），也必须进入手持规划。
            // 4. pawn.carryTracker.CarriedThing == null: 确保我们不会在小人已经拿着东西时，再给他规划一个。
            bool needsHandheldPlanning = (wasBackpackFilled || backpackIsFullOrOverweight || hasUnbackpackableThings)
                                       && pawn.carryTracker.CarriedThing == null;
            DebugLogger.LogMessage(LogCategory.Planner, () => $"  - Checking handheld planning conditions: wasBackpackFilled={wasBackpackFilled}, backpackIsFullOrOverweight={backpackIsFullOrOverweight}, hasUnbackpackableThings={hasUnbackpackableThings}, isHandEmpty={pawn.carryTracker.CarriedThing == null}. Result: {needsHandheldPlanning}.");

            if (needsHandheldPlanning)
            {
                DebugLogger.LogMessage(LogCategory.Planner, () => "  - Phase 3: Starting handheld planning.");
                var candidatePool = new List<HaulCandidate>();

                // 1. 添加剩余的可入包物品
                foreach (var analysis in demandAnalyses)
                {
                    if (analysis.IdealNeeded > 0)
                        foreach (var source in analysis.IdealSources.Where(s => remainingStackCounts.ContainsKey(s) && remainingStackCounts[s] > 0))
                            candidatePool.Add(new HaulCandidate { Demand = analysis.OriginalDemand, Source = source });

                    if (analysis.AlternativeNeeded > 0)
                        foreach (var source in analysis.AlternativeSources.Where(s => remainingStackCounts.ContainsKey(s) && remainingStackCounts[s] > 0))
                            candidatePool.Add(new HaulCandidate { Demand = analysis.OriginalDemand, Source = source });
                }

                // 2. 添加所有不可入包物品
                foreach (var kvp in unbackpackableSourcesByDemand)
                {
                    foreach (var source in kvp.Value.Where(s => remainingStackCounts.ContainsKey(s) && remainingStackCounts[s] > 0))
                    {
                        // 双重保险：再次确认这个Pawn不是自主登船者
                        if (source is Pawn p && !LoadTransporters_WorkGiverUtility.NeedsToBeCarried(p)) continue;
                        candidatePool.Add(new HaulCandidate { Demand = kvp.Key, Source = source });
                    }
                }
                DebugLogger.LogMessage(LogCategory.Planner, () => $"    - Handheld candidate pool created with {candidatePool.Count} entries.");


                if (candidatePool.Any())
                {
                    HaulCandidate bestChoice = FindBestChoiceFromCandidates(pawn, candidatePool, pathingBudgetUsed, settings.pathfindingHeuristicCandidates, pathingOrigin);

                    if (bestChoice != null)
                    {
                        DebugLogger.LogMessage(LogCategory.Planner, () => $"    - Best handheld choice is: {bestChoice.Source.LabelCap} from {bestChoice.Source.Position}.");
                        int needed;

                        if (BulkLoad_Utility.IsUnbackpackable(bestChoice.Source))
                        {
                            // --- 对于不可入包物品 (如Pawn)，需求是绝对的、不可替代的 ---
                            // 我们只关心 bestChoice 对应的那个原始 Transferable 的需求量。
                            needed = bestChoice.Demand.CountToTransfer;
                        }
                        else
                        {
                            // --- 对于可入包物品，我们可以聚合所有剩余需求 ---
                            var relevantAnalyses = demandAnalyses.Where(a => a.OriginalDemand.ThingDef == bestChoice.Source.def);
                            needed = relevantAnalyses.Sum(a => a.IdealNeeded + a.AlternativeNeeded);
                        }

                        int amountToTake = Mathf.Min(
                            remainingStackCounts[bestChoice.Source],
                            needed,
                            constraints.TryGetValue(bestChoice.Source.def, 0)
                        );
                        DebugLogger.LogMessage(LogCategory.Planner, () => $"      - Handheld needed: {needed}, Available: {remainingStackCounts[bestChoice.Source]}, Manager: {constraints.TryGetValue(bestChoice.Source.def, 0)}. Final amountToTake: {amountToTake}.");

                        if (amountToTake > 0)
                        {
                            shoppingBasket.Add(new KeyValuePair<Thing, int>(bestChoice.Source, amountToTake));
                        }
                    }
                    else
                    {
                        DebugLogger.LogMessage(LogCategory.Planner, () => "    - FindBestChoiceFromCandidates for handheld returned null.");
                    }
                }
            }
            DebugLogger.LogMessage(LogCategory.Planner, () => "--- Finished ProcessDemands_Optimized ---");
        } // <--- 方法结束

        // 这是一个私有辅助方法，是“混合路径规划”算法的核心。
        // 它根据用户的设置，在“速度优先”（只看直线距离）和“精度优先”（真实寻路）之间进行动态切换。
        private static HaulCandidate FindBestChoiceFromCandidates(
            Pawn pawn,
            List<HaulCandidate> candidatePool,
            int pathingBudgetUsed,
            int candidateCountSetting,
            IntVec3 pathingOrigin)
        {
            if (!candidatePool.Any()) return null;
            DebugLogger.LogMessage(LogCategory.Planner, () => $"FindBestChoiceFromCandidates called. Pool size: {candidatePool.Count}, Budget used: {pathingBudgetUsed}, N setting: {candidateCountSetting}, Origin: {pathingOrigin}.");

            HaulCandidate bestChoice = null;

            // NOTE: 这是“速度优先”模式的开关。
            // 当用户在设置中将候选者数量(N)设为1时，我们完全跳过昂贵的寻路计算，
            // 只依赖于启发式（直线距离），以实现最快的规划速度。
            bool usePureHeuristic = candidateCountSetting <= 1;
            bool usePrecisePathing = !usePureHeuristic && (pathingBudgetUsed < candidateCountSetting);
            DebugLogger.LogMessage(LogCategory.Planner, () => $"  - Pathing mode selected: {(usePrecisePathing ? "Precise (Budgeted)" : "Pure Heuristic")}.");

            if (usePrecisePathing)
            {
                // NOTE: 这是“海选”阶段。我们先用廉价的直线距离，从所有可能的候选中，
                // 快速筛选出 N 个最有希望的候选者，放入“决选名单”(shortlist)。
                var shortlist = new List<HaulCandidate>(candidateCountSetting + 1);
                DebugLogger.LogMessage(LogCategory.Planner, () => "  - Precise Mode: Starting shortlist selection (the 'audition')...");
                foreach (var candidate in candidatePool)
                {
                    candidate.HeuristicDistanceSq = pathingOrigin.DistanceToSquared(candidate.Source.Position);
                    if (shortlist.Count < candidateCountSetting || candidate.HeuristicDistanceSq < shortlist.Last().HeuristicDistanceSq)
                    {
                        shortlist.Add(candidate);
                        shortlist.Sort((a, b) => a.HeuristicDistanceSq.CompareTo(b.HeuristicDistanceSq));
                        if (shortlist.Count > candidateCountSetting)
                            shortlist.RemoveAt(candidateCountSetting);
                    }
                }
                DebugLogger.LogMessage(LogCategory.Planner, () => {
                    var sb = new StringBuilder();
                    sb.AppendLine($"    - Shortlist generated! Top {shortlist.Count} candidates (by heuristic distance):");
                    foreach (var c in shortlist) sb.AppendLine($"      - {c.Source.LabelCap} at {c.Source.Position} (DistSq: {c.HeuristicDistanceSq:F0})");
                    return sb.ToString();
                });

                // NOTE: 这是“决选”阶段。我们只对“决选名单”中的少数候选者，
                // 调用真正消耗性能的寻路算法 (FindPathNow)，找到真正的最优解。
                float minPathCost = float.MaxValue;
                DebugLogger.LogMessage(LogCategory.Planner, () => "  - Precise Mode: Starting pathfinding on shortlist (the 'final round')...");
                foreach (var candidate in shortlist)
                {
                    using (PawnPath path = pawn.Map.pathFinder.FindPathNow(pathingOrigin, candidate.Source, pawn))
                    {
                        if (path != PawnPath.NotFound)
                        {
                            DebugLogger.LogMessage(LogCategory.Planner, () => $"    - Path cost for '{candidate.Source.LabelCap}'? {path.TotalCost:F0}");
                            if (path.TotalCost < minPathCost)
                            {
                                // 如果 minPathCost 仍然是初始值，就显示一个更友好的文本
                                string previousBestCost = (minPathCost == float.MaxValue)
                                    ? "(none yet)"
                                    : minPathCost.ToString("F0");
                                DebugLogger.LogMessage(LogCategory.Planner, () => $"    - New best choice found! '{candidate.Source.LabelCap}' (cost {path.TotalCost:F0}) is better than previous best (cost {previousBestCost}).");

                                minPathCost = path.TotalCost;
                                bestChoice = candidate;
                            }
                        }
                        else
                        {
                            DebugLogger.LogMessage(LogCategory.Planner, () => $"    - Path NOT found for {candidate.Source.LabelCap}.");
                        }
                    }
                }
            }
            else
            {
                // --- 纯粹启发式 (N<=1 或 预算耗尽) ---
                DebugLogger.LogMessage(LogCategory.Planner, () => "  - Heuristic Mode: Finding closest reachable target by straight-line distance.");
                float minHeuristicDistSq = float.MaxValue;
                foreach (var candidate in candidatePool)
                {
                    float distSq = pathingOrigin.DistanceToSquared(candidate.Source.Position);
                    if (distSq < minHeuristicDistSq)
                    {
                        // 可达性预检
                        if (pawn.CanReach(candidate.Source, PathEndMode.ClosestTouch, Danger.Deadly))
                        {
                            minHeuristicDistSq = distSq;
                            bestChoice = candidate;
                        }
                    }
                }
                if (bestChoice != null)
                {
                    DebugLogger.LogMessage(LogCategory.Planner, () => $"  - Best choice via heuristic: '{bestChoice.Source.LabelCap}' at {bestChoice.Source.Position}.");
                }
            }

            if (bestChoice == null)
            {
                DebugLogger.LogMessage(LogCategory.Planner, () => "-> FindBestChoiceFromCandidates finished with NO valid choice.");
            }
            else
            {
                DebugLogger.LogMessage(LogCategory.Planner, () => $"-> FindBestChoiceFromCandidates finished. Final choice: '{bestChoice.Source.LabelCap}'.");
            }
            return bestChoice;
        }
    }
}