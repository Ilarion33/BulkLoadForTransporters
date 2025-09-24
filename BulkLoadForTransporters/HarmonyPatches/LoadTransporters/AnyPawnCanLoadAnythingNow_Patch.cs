// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadTransporters/AnyPawnCanLoadAnythingNow_Patch.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Interfaces;
using HarmonyLib;
using RimWorld;
using System.Linq;
using Verse;

namespace BulkLoadForTransporters.HarmonyPatches.LoadTransporters
{
    /// <summary>
    /// Patches the `CompTransporter.AnyPawnCanLoadAnythingNow` property getter.
    /// This prevents false "stalled" warnings when a bulk load job is in progress.
    /// </summary>
    [HarmonyPatch(typeof(CompTransporter), "get_AnyPawnCanLoadAnythingNow")]
    public static class CompTransporter_AnyPawnCanLoadAnythingNow_Patch
    {
        public static bool Prepare()
        {
            return LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>().enableBulkLoadTransporters;
        }

        /// <summary>
        /// A Prefix patch that checks for active bulk loading jobs.
        /// If a pawn is found running our custom job for this transporter group,
        /// it immediately returns `true` to signal that work is in progress.
        /// </summary>
        public static bool Prefix(CompTransporter __instance, ref bool __result)
        {
            var map = __instance.parent.Map;
            if (map == null)
            {
                return true; 
            }

            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var pawn = pawns[i];

                // 使用 JobDefRegistry 提供的分类来精确识别我们的装载任务。
                if (pawn.CurJob?.def == JobDefRegistry.LoadTransporters)
                {
                    var jobTarget = pawn.CurJob?.targetB.Thing;
                    if (jobTarget == null) continue;

                    // 检查这个 Job 的目标是否是当前运输仓组的一员
                    var transportersInGroup = __instance.TransportersInGroup(map);
                    if (transportersInGroup != null && transportersInGroup.Any(tr => tr.parent == jobTarget))
                    {
                        // 找到了一个正在为这个组执行我们系统任务的小人。
                        __result = true;
                        return false;
                    }
                }
            }

            return true;
        }
    }
}