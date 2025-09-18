// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadTransporters/TransporterUtility_AllSendables_Patch.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Interfaces;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace BulkLoadForTransporters.HarmonyPatches
{
    /// <summary>
    /// Patches the utility methods that gather items and pawns for the transporter loading dialog.
    /// </summary>
    [HarmonyPatch(typeof(TransporterUtility))]
    public static class TransporterUtility_AllSendables_Patch
    {
        public static bool Prepare()
        {
            return LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkLoadTransporters;
        }

        /// <summary>
        /// Postfix to inject haulable ITEMS from our active haulers' inventories.
        /// </summary>
        [HarmonyPatch("AllSendableItems")]
        [HarmonyPostfix]
        public static void AllSendableItems_Postfix(ref IEnumerable<Thing> __result, List<CompTransporter> transporters, Map map)
        {
            // 调用共享的辅助方法，并传入一个“非Pawn”的过滤器
            __result = InjectHauledThings(__result, transporters, map, thing => !(thing is Pawn));
        }

        /// <summary>
        /// Postfix to inject haulable PAWNS from our active haulers' inventories.
        /// </summary>
        [HarmonyPatch("AllSendablePawns")]
        [HarmonyPostfix]
        public static void AllSendablePawns_Postfix(ref IEnumerable<Pawn> __result, List<CompTransporter> transporters, Map map)
        {
            __result = InjectHauledThings(__result, transporters, map, thing => thing is Pawn).Cast<Pawn>();
        }

        /// <summary>
        /// The shared core logic that finds active haulers for a given transporter group
        /// and injects their carried items into the result list.
        /// </summary>
        /// <param name="originalResult">The original list of sendable things from the vanilla method.</param>
        /// <param name="transporters">The transporter group context for the dialog.</param>
        /// <param name="map">The current map.</param>
        /// <param name="filter">A predicate to filter for either items or pawns.</param>
        /// <returns>A new list containing both original and injected things.</returns>
        private static IEnumerable<Thing> InjectHauledThings(IEnumerable<Thing> originalResult, List<CompTransporter> transporters, Map map, System.Predicate<Thing> filter)
        {
            if (originalResult == null || transporters.NullOrEmpty() || map == null)
            {
                return originalResult;
            }

            var firstTransporter = transporters.FirstOrDefault();
            if (firstTransporter == null) return originalResult;
            int groupID = firstTransporter.groupID;
            if (groupID < 0) return originalResult;

            // NOTE: 获取ShuttleComp
            var shuttleComp = firstTransporter.parent.TryGetComp<CompShuttle>();

            var resultList = originalResult.ToList();
            var existingThings = new HashSet<Thing>(resultList);

            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                // 使用我们集中的JobDef注册表来识别正在执行装载任务的小人。
                if (pawn.CurJob == null || pawn.CurJob.def != JobDefRegistry.LoadTransporters)
                {
                    continue;
                }

                var jobTarget = pawn.CurJob.targetB.Thing;
                var jobTransporter = jobTarget?.TryGetComp<CompTransporter>();

                // 确保这个小人正在为我们当前打开的这个运输仓组工作。
                if (jobTransporter != null && jobTransporter.groupID == groupID)
                {
                    var thingsToInject = new HashSet<Thing>();

                    // 信任并收集来自逻辑状态 (HauledThings) 的物品
                    if (pawn.jobs.curDriver is IBulkHaulState bulkHaulState && bulkHaulState.HauledThings.Any())
                    {
                        foreach (var thing in bulkHaulState.HauledThings)
                        {
                            thingsToInject.Add(thing);
                        }
                    }

                    // 其次，我们对小人手上物理拿着的物品进行一次额外的审计。
                    var carriedThing = pawn.carryTracker.CarriedThing;
                    if (carriedThing != null)
                    {
                        if (shuttleComp != null && shuttleComp.IsRequired(carriedThing))
                        {
                            thingsToInject.Add(carriedThing);
                        }
                    }

                    // 对所有收集到的、经过验证的物品进行统一的注入处理
                    foreach (var thing in thingsToInject)
                    {
                        if (thing != null && !thing.Destroyed && filter(thing) && existingThings.Add(thing))
                        {
                            resultList.Add(thing);
                        }
                    }
                }
            }
            return resultList;
        }
    }
}