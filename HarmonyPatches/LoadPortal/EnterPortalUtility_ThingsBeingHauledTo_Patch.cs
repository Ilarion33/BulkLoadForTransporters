// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadPortal/EnterPortalUtility_ThingsBeingHauledTo_Patch.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Interfaces;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace BulkLoadForTransporters.HarmonyPatches.LoadPortal
{
    // NOTE: This patch injects items from our "courier" pawns' inventories into the list of things considered "on the way" to a portal.
    // This ensures they appear in the loading dialog window (Dialog_EnterPortal).

    /// <summary>
    /// Patches the data source for the portal loading dialog to include items being carried by pawns performing our custom bulk loading jobs.
    /// </summary>
    [HarmonyPatch(typeof(EnterPortalUtility), "ThingsBeingHauledTo")]
    public static class EnterPortalUtility_ThingsBeingHauledTo_Patch
    {
        public static bool Prepare()
        {
            return LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkLoadPortal;
        }

        /// <summary>
        /// After the original method runs, we find our "courier" pawns and add their hauled items to the result.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(ref IEnumerable<Thing> __result, MapPortal portal)
        {
            if (portal == null || portal.Map == null) return;

            var resultList = __result.ToList();
            var existingThings = new HashSet<Thing>(resultList); 

            // 寻找所有正在为这个 portal 工作的“快递员”
            foreach (var pawn in portal.Map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.CurJob?.def == JobDefRegistry.LoadPortal && pawn.CurJob?.targetB.Thing == portal)
                {
                    if (pawn.jobs.curDriver is IBulkHaulState bulkHaulState && bulkHaulState.HauledThings.Any())
                    {
                        foreach (var thing in bulkHaulState.HauledThings)
                        {
                            // 确保不重复添加
                            if (thing != null && !thing.Destroyed && existingThings.Add(thing))
                            {
                                resultList.Add(thing);
                            }
                        }
                    }
                }
            }

            // 将修改后的列表写回 __result
            __result = resultList;
        }
    }
}