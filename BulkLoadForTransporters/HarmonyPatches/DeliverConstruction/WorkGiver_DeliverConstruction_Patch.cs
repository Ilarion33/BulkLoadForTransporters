// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/DeliverConstruction/WorkGiver_DeliverConstruction_Patch.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Utils;
using HarmonyLib;
using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.HarmonyPatches.DeliverConstruction
{
    // --- Patch 1: The Sentry (Now with Deception Control) ---
    [HarmonyPatch(typeof(WorkGiver_Scanner), "HasJobOnThing")]
    public static class WorkGiver_Scanner_HasJobOnThing_ConstructionPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result, WorkGiver_Scanner __instance, Pawn pawn, Thing t)
        {
            Type instanceType = __instance.GetType();
            if (instanceType != typeof(WorkGiver_ConstructDeliverResourcesToBlueprints) &&
                instanceType != typeof(WorkGiver_ConstructDeliverResourcesToFrames))
            {
                return true; // 不是我们的目标，完全放行
            }

            if (Compatibility_Utility.IsReplaceStuffFrame(t))
            {
                return true; // For ReplaceStuff
            }

            if (!(__instance is WorkGiver_ConstructDeliverResources)) return true;
            if (!LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkConstructionDelivery) return true;
            if (!(t is IConstructible constructible)) return true;
            if (t is Blueprint_Install)
            {
                return true; // 是安装任务，立即放行
            }

            if (!JobDriver_Utility.CanPawnWorkOnSite(pawn, constructible))
            {
                __result = false;
                return false;
            }

            // --- Activate Deception Protocol ---
            OptimisticHaulingController.IsInBulkPlanningPhase = true;
            try
            {
                // Perform all hard-fail checks. CanConstruct will now be "deceived" about blocking things.
                if (t.Faction != pawn.Faction) return true;

                if (!GenConstruct.CanTouchTargetFromValidCell(t, pawn))
                {
                    __result = false;
                    return false;
                }

                if (!GenConstruct.CanConstruct(t, pawn, true, false, JobDefRegistry.DeliverToConstruction))
                {
                    __result = false;
                    return false;
                }
                
                // All non-blocking checks passed. Now, our fast bulk check.
                var adapter = new ConstructionGroupAdapter(constructible, pawn);
                __result = WorkGiver_Utility.HasPotentialBulkWork(pawn, adapter);
            }
            finally
            {
                // --- Deactivate Deception Protocol ---
                OptimisticHaulingController.IsInBulkPlanningPhase = false;
            }

            return false; // We provide the final answer.
        }
    }


    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), "JobOnThing")]
    public static class WorkGiver_Blueprints_JobOnThing_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref Job __result, Pawn pawn, Thing t)
        {
            if (!LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkConstructionDelivery)
            {
                return true;
            }

            if (Compatibility_Utility.IsReplaceStuffFrame(t))
            {
                return true; // For ReplaceStuff
            }

            if (t is Blueprint_Install)
            {
                return true; // 是安装任务，立即放行
            }

            if (t is Blueprint blueprint && !(t is Blueprint_Install) && blueprint.TotalMaterialCost().Count == 0)
            {
                // 这是一个无成本的建造蓝图。
                __result = JobMaker.MakeJob(JobDefOf.PlaceNoCostFrame, blueprint);
                return false;
            }

            OptimisticHaulingController.IsInBulkPlanningPhase = true;
            try
            {
                var adapter = new ConstructionGroupAdapter(t as IConstructible, pawn);
                WorkGiver_Utility.TryGiveBulkJob(pawn, adapter, out __result);
            }
            finally
            {
                // --- 确保在任何情况下都关闭欺骗协议 ---
                OptimisticHaulingController.IsInBulkPlanningPhase = false;
            }

            return false; // 阻止原方法
        }
    }

    // --- 补丁 2b: 专门针对框架 ---
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToFrames), "JobOnThing")]
    public static class WorkGiver_Frames_JobOnThing_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref Job __result, Pawn pawn, Thing t)
        {
            if (!LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkConstructionDelivery)
            {
                return true;
            }

            if (Compatibility_Utility.IsReplaceStuffFrame(t))
            {
                return true; // For ReplaceStuff
            }

            if (t is Blueprint_Install)
            {
                return true; // 是安装任务，立即放行
            }

            OptimisticHaulingController.IsInBulkPlanningPhase = true;
            try
            {
                var adapter = new ConstructionGroupAdapter(t as IConstructible, pawn);
                WorkGiver_Utility.TryGiveBulkJob(pawn, adapter, out __result);
            }
            finally
            {
                // --- 确保在任何情况下都关闭欺骗协议 ---
                OptimisticHaulingController.IsInBulkPlanningPhase = false;
            }

            return false; // 阻止原方法
        }
    }
}