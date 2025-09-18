// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/WorkGiver_UnloadCarriers_Patch.cs
using BulkLoadForTransporters.Core;
using HarmonyLib;
using RimWorld;
using System.Linq;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.HarmonyPatches.UnloadCarriers
{
    /// <summary>
    /// Overrides `WorkGiver_UnloadCarriers` to replace its single-item unload job with our own multi-item bulk unload job.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_UnloadCarriers))]
    public static class WorkGiver_UnloadCarriers_Patch
    {
        /// <summary>
        /// Determines if a pawn is of a type that should be handled exclusively by vanilla logic.
        /// </summary>
        private static bool ShouldLetVanillaHandle(Pawn pawn)
        {            
            if (pawn.RaceProps.IsMechanoid && !PickUpAndHaul.Settings.AllowMechanoids)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// The core decision logic. After taking over from vanilla, this method checks
        /// if a bulk unload job is truly feasible.
        /// </summary>
        private static bool CanDoBulkUnload(Pawn pawn, Thing t, bool forced)
        {
            // 复用原版工具类进行基础检查 (例如，目标是否标记了 UnloadEverything)，确保我们遵循游戏的基本规则。
            if (!UnloadCarriersJobGiverUtility.HasJobOnThing(pawn, t, forced))
            {
                return false;
            }

            // 检查小人手上是否已拿着东西。
            if (pawn.carryTracker.CarriedThing != null)
            {
                return false;
            }

            // 检查小人的背包是否有足够的剩余空间，以满足设置中的门槛。
            var settings = LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>();
            float maxEncumbranceThreshold = 1f - settings.minFreeSpaceToUnloadCarrierPct;
            if (MassUtility.EncumbrancePercent(pawn) > maxEncumbranceThreshold)
            {
                return false;
            }

            Pawn carrier = t as Pawn;
            if (carrier == null || !carrier.inventory.innerContainer.Any)
            {
                return false;
            }

            // 检查是否已经有其他小人正在或即将去执行这个任务
            var unloadJobDef = JobDefRegistry.UnloadCarriers;
            foreach (var otherPawn in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (otherPawn != pawn && otherPawn.CurJob != null && otherPawn.CurJob.def == unloadJobDef)
                {
                    // 如果另一个小人的目标就是我们正在考虑的这个驮兽
                    if (otherPawn.CurJob.GetTarget(TargetIndex.A).Thing == t)
                    {
                        return false; 
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Overrides the vanilla check for whether a job exists.
        /// </summary>
        [HarmonyPatch("HasJobOnThing")]
        [HarmonyPrefix]
        public static bool HasJobOnThing_Prefix(ref bool __result, Pawn pawn, Thing t, bool forced = false)
        {
            if (!LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkUnloadCarriers)
            {
                return true;
            }

            // 责任划分：决定这个任务是由我们处理，还是应该交还给原版。
            if (ShouldLetVanillaHandle(pawn))
            {
                return true; 
            }

            // 执行我们自己的决策
            __result = CanDoBulkUnload(pawn, t, forced);
            return false; 
        }

        /// <summary>
        /// Overrides the vanilla job creation with our own.
        /// </summary>
        [HarmonyPatch("JobOnThing")]
        [HarmonyPrefix]
        public static bool JobOnThing_Prefix(ref Job __result, Pawn pawn, Thing t, bool forced = false)
        {
            if (!LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkUnloadCarriers)
            {
                return true;
            }
            if (ShouldLetVanillaHandle(pawn))
            {
                return true; 
            }

            if (CanDoBulkUnload(pawn, t, forced))
            {
                __result = JobMaker.MakeJob(JobDefRegistry.UnloadCarriers, t);
            }
            else
            {
                __result = null;
            }
            return false; 
        }
    }
}