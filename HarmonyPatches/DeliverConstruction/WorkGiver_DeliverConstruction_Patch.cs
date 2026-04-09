//// Copyright (c) 2025 Ilarion. All rights reserved.
////
//// HarmonyPatches/DeliverConstruction/WorkGiver_DeliverConstruction_Patch.cs
//using BulkLoadForTransporters.Core;
//using BulkLoadForTransporters.Core.Adapters;
//using BulkLoadForTransporters.Core.Utils;
//using HarmonyLib;
//using RimWorld;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using Verse;
//using Verse.AI;

//namespace BulkLoadForTransporters.HarmonyPatches.DeliverConstruction
//{
//    // --- Patch 1: The Sentry (Now with Deception Control) ---
//    [HarmonyPatch(typeof(WorkGiver_Scanner), "HasJobOnThing")]
//    public static class WorkGiver_Scanner_HasJobOnThing_ConstructionPatch
//    {
//        [HarmonyPrefix]
//        public static bool Prefix(ref bool __result, WorkGiver_Scanner __instance, Pawn pawn, Thing t)
//        {
//            if (pawn == null || t == null || !t.Spawned || t.Map == null || pawn.Map == null || pawn.Map != t.Map)
//            {
//                return true;
//            }

//            Type instanceType = __instance.GetType();
//            if (instanceType != typeof(WorkGiver_ConstructDeliverResourcesToBlueprints) &&
//                instanceType != typeof(WorkGiver_ConstructDeliverResourcesToFrames))
//            {
//                return true; // 不是我们的目标，完全放行
//            }

//            if (Compatibility_Utility.IsIncompatibleConstructionThing(t))
//            {
//                return true; // For ReplaceStuff
//            }

//            if (!(__instance is WorkGiver_ConstructDeliverResources)) return true;
//            if (!LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkConstructionDelivery) return true;
//            if (!(t is IConstructible constructible)) return true;
//            if (constructible == null)
//            {
//                // 这个 Thing 不是我们能理解的 IConstructible，立即放行，让原版或其他 Mod 处理。
//                return true;
//            }

//            if (t is Blueprint blueprint && !(t is Blueprint_Install) && blueprint.TotalMaterialCost().Count == 0)
//            {
//                // 这是一个无成本的建造蓝图。
//                return true;
//            }

//            if (!JobDriver_Utility.CanPawnWorkOnSite(pawn, constructible))
//            {
//                __result = false;
//                return false;
//            }

//            // --- Activate Deception Protocol ---
//            OptimisticHaulingController.IsInBulkPlanningPhase = true;
//            try
//            {
//                // Perform all hard-fail checks. CanConstruct will now be "deceived" about blocking things.
//                if (t.Faction != pawn.Faction) return true;

//                if (!GenConstruct.CanTouchTargetFromValidCell(t, pawn))
//                {
//                    __result = false;
//                    return false;
//                }

//                if (!GenConstruct.CanConstruct(t, pawn, true, false, JobDefRegistry.DeliverToConstruction))
//                {
//                    __result = false;
//                    return false;
//                }

//                // All non-blocking checks passed. Now, our fast bulk check.
//                var adapter = ConstructionGroupAdapter.TryCreate(constructible, pawn);
//                if (adapter == null)
//                {
//                    // 如果工厂方法因为“超廉价”检查而返回 null，说明这里肯定没活干。
//                    __result = false;
//                    return false;
//                }


//                bool hasWork = WorkGiver_Utility.HasPotentialBulkWork(pawn, adapter);

//                // --- 植入“失败原因”分析与报告逻辑 ---
//                if (!hasWork && FloatMenuMakerMap.makingFor == pawn)
//                {
//                    // HasPotentialBulkWork 报告失败，并且我们正在为右键菜单工作。
//                    // 现在，我们自己进行一次轻量级的“物资盘点”，以找出原因。

//                    var missingMaterials = new Dictionary<ThingDef, int>();
//                    var neededNow = adapter.GetThingsToLoad(); // 获取的是经过“小人能力”过滤后的净需求

//                    if (neededNow != null)
//                    {
//                        foreach (var need in neededNow)
//                        {
//                            // 1. 廉价检查：地图上是否存在任何未被禁用的实例？
//                            var potentialSources = pawn.Map.listerThings.ThingsOfDef(need.thingDef)
//                                                       .Where(res => !res.IsForbidden(pawn));

//                            if (!potentialSources.Any())
//                            {
//                                // 如果连一个实例都找不到，那肯定是缺失了。
//                                missingMaterials[need.thingDef] = need.count;
//                                continue; // 继续检查下一种材料
//                            }

//                            // 2. 昂贵检查：在所有潜在来源中，是否存在至少一个可达的？
//                            //    这个 Any() 调用现在只会在“可能”有希望的情况下执行一次昂贵的 CanReach。
//                            if (!potentialSources.Any(res => pawn.CanReach(res, PathEndMode.ClosestTouch, Danger.Deadly)))
//                            {
//                                missingMaterials[need.thingDef] = need.count;
//                            }
//                        }
//                    }

//                    if (missingMaterials.Any())
//                    {
//                        // 找到了缺失的物资！模仿原版逻辑，设置 JobFailReason。
//                        JobFailReason.Is("MissingMaterials".Translate((from kvp in missingMaterials
//                                                                       select string.Format("{0}x {1}", kvp.Value, kvp.Key.label)).ToCommaList(false, false)), null);
//                    }
//                    // 如果 !missing.Any()，意味着失败是其他原因（例如，所有物资都被认领了），
//                    // 在这种情况下，我们不设置 JobFailReason，让右键菜单不显示任何东西，这是正确的行为。
//                }

//                __result = hasWork;
//            }
//            finally
//            {
//                // --- Deactivate Deception Protocol ---
//                OptimisticHaulingController.IsInBulkPlanningPhase = false;
//            }

//            return false; // We provide the final answer.
//        }
//    }


//    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), "JobOnThing")]
//    public static class WorkGiver_Blueprints_JobOnThing_Patch
//    {
//        [HarmonyPrefix]
//        public static bool Prefix(ref Job __result, Pawn pawn, Thing t)
//        {
//            var constructible = t as IConstructible;
//            if (constructible == null)
//            {
//                // 这个 Thing 不是我们能理解的 IConstructible，立即放行，让原版或其他 Mod 处理。
//                return true;
//            }

//            if (!LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkConstructionDelivery)
//            {
//                return true;
//            }

//            if (Compatibility_Utility.IsIncompatibleConstructionThing(t))
//            {
//                return true; // For ReplaceStuff
//            }

//            if (t is Blueprint blueprint && !(t is Blueprint_Install) && blueprint.TotalMaterialCost().Count == 0)
//            {
//                // 这是一个无成本的建造蓝图。
//                __result = JobMaker.MakeJob(JobDefOf.PlaceNoCostFrame, blueprint);
//                return false;
//            }

//            OptimisticHaulingController.IsInBulkPlanningPhase = true;
//            try
//            {
//                var adapter = ConstructionGroupAdapter.TryCreate(t as IConstructible, pawn);
//                WorkGiver_Utility.TryGiveBulkJob(pawn, adapter, out __result);
//            }
//            finally
//            {
//                // --- 确保在任何情况下都关闭欺骗协议 ---
//                OptimisticHaulingController.IsInBulkPlanningPhase = false;
//            }

//            return false; // 阻止原方法
//        }
//    }

//    // --- 补丁 2b: 专门针对框架 ---
//    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToFrames), "JobOnThing")]
//    public static class WorkGiver_Frames_JobOnThing_Patch
//    {
//        [HarmonyPrefix]
//        public static bool Prefix(ref Job __result, Pawn pawn, Thing t)
//        {
//            var constructible = t as IConstructible;
//            if (constructible == null)
//            {
//                // 这个 Thing 不是我们能理解的 IConstructible，立即放行，让原版或其他 Mod 处理。
//                return true;
//            }


//            if (!LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkConstructionDelivery)
//            {
//                return true;
//            }

//            if (Compatibility_Utility.IsIncompatibleConstructionThing(t))
//            {
//                return true; // For ReplaceStuff
//            }

//            OptimisticHaulingController.IsInBulkPlanningPhase = true;
//            try
//            {
//                var adapter = ConstructionGroupAdapter.TryCreate(t as IConstructible, pawn);
//                WorkGiver_Utility.TryGiveBulkJob(pawn, adapter, out __result);
//            }
//            finally
//            {
//                // --- 确保在任何情况下都关闭欺骗协议 ---
//                OptimisticHaulingController.IsInBulkPlanningPhase = false;
//            }

//            return false; // 阻止原方法
//        }
//    }
//}