// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadPortal/WorkGiver_LoadPortal_Patch.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.HarmonyPatches.LoadPortal
{
    /// <summary>
    /// Patches EnterPortalUtility to redirect the AI's hauling jobs for portals to our own bulk loading system.
    /// </summary>
    [HarmonyPatch(typeof(EnterPortalUtility))]
    public static class WorkGiver_LoadPortal_Patch
    {
        public static bool Prepare()
        {
            return LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>().enableBulkLoadPortal;
        }

        /// <summary>
        /// Replaces the vanilla logic for determining if a hauling job for a portal exists.
        /// </summary>
        [HarmonyPatch("HasJobOnPortal")]
        [HarmonyPrefix]
        public static bool HasJobOnPortal_Prefix(ref bool __result, Pawn pawn, MapPortal portal)
        {
            // 创建特定于 MapPortal 的 Adapter 实例
            IManagedLoadable groupLoadable = new MapPortalAdapter(portal);

            __result = WorkGiver_Utility.HasPotentialBulkWork(pawn, groupLoadable);

            return false;
        }

        /// <summary>
        /// Replaces the vanilla logic for creating a hauling job for a portal.
        /// </summary>
        [HarmonyPatch("JobOnPortal")]
        [HarmonyPrefix]
        public static bool JobOnPortal_Prefix(ref Job __result, Pawn p, MapPortal portal)
        {
            IManagedLoadable groupLoadable = new MapPortalAdapter(portal);

            WorkGiver_Utility.TryGiveBulkJob(p, groupLoadable, out __result);

            return false;
        }
    }
}