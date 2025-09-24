// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadPortal/MapPortal_AnyPawnCanLoadAnythingNow_Patch.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Interfaces;
using HarmonyLib;
using RimWorld;
using System.Linq;
using Verse;

namespace BulkLoadForTransporters.HarmonyPatches.LoadPortal
{
    // NOTE: This patch prevents the warning message from appearing incorrectly when our custom bulk loading jobs are in progress for a MapPortal.

    /// <summary>
    /// Patches the MapPortal.AnyPawnCanLoadAnythingNow property to recognize our custom loading jobs.
    /// </summary>
    [HarmonyPatch(typeof(MapPortal), "get_AnyPawnCanLoadAnythingNow")]
    public static class MapPortal_AnyPawnCanLoadAnythingNow_Patch
    {
        public static bool Prepare()
        {
            // 这个补丁也应该受到总开关的控制
            return LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>().enableBulkLoadPortal;
        }

        /// <summary>
        /// Before the original property getter runs, we check for our own jobs first.
        /// </summary>
        public static bool Prefix(MapPortal __instance, ref bool __result)
        {
            var map = __instance.Map;
            if (map == null)
            {
                return true;
            }

            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var pawn = pawns[i];
                                
                if (pawn.CurJob?.def == JobDefRegistry.LoadPortal)
                {
                    // 检查是否有任何Pawn正在执行我们为传送门定制的批量装载Job。
                    if (pawn.CurJob?.targetB.Thing == __instance)
                    {
                        __result = true;
                        return false;
                    }
                }
            }

            return true;
        }
    }
}