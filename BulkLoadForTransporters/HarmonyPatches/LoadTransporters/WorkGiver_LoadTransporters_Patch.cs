// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadTransporters/WorkGiver_LoadTransporters_Patch.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.HarmonyPatches.LoadTransporters
{
    /// <summary>
    /// Intercepts the vanilla utility class used by the WorkGiver for transporters,
    /// replacing its logic with our bulk-aware system.
    /// </summary>
    [HarmonyPatch(typeof(LoadTransportersJobUtility))]
    public static class WorkGiver_LoadTransporters_Patch
    {
        /// <summary>
        /// A prefix to replace the vanilla "has job" check.
        /// </summary>
        [HarmonyPatch("HasJobOnTransporter")]
        [HarmonyPrefix]
        public static bool HasJobOnTransporter_Prefix(ref bool __result, Pawn pawn, CompTransporter transporter)
        {
            if (!LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkLoadTransporters)
            {
                return true;
            }

            IManagedLoadable groupLoadable = new LoadTransportersAdapter(transporter);
            __result = LoadTransporters_WorkGiverUtility.HasPotentialBulkWork(pawn, groupLoadable);
            return false;
        }

        /// <summary>
        /// A prefix to replace the vanilla job creation logic.
        /// </summary>
        [HarmonyPatch("JobOnTransporter")]
        [HarmonyPrefix]
        public static bool JobOnTransporter_Prefix(ref Job __result, Pawn p, CompTransporter transporter)
        {
            if (!LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkLoadTransporters)
            {
                return true;
            }

            IManagedLoadable groupLoadable = new LoadTransportersAdapter(transporter);
            LoadTransporters_WorkGiverUtility.TryGiveBulkJob(p, groupLoadable, out __result);
            return false;
        }
    }
}