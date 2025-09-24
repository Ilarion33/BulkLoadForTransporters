// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadTransporters/BoardingInventorySync_Patch.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Utils;
using HarmonyLib;
using PickUpAndHaul;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace BulkLoadForTransporters.HarmonyPatches.LoadTransporters
{
    /// <summary>
    /// Patches `CompTransporter.Notify_ThingAdded` to sync a boarding pawn's PUAH inventory with the transporter's cargo needs.
    /// </summary>
    [HarmonyPatch(typeof(CompTransporter), "Notify_ThingAdded")]
    public static class BoardingInventorySync_Patch
    {
        public static bool Prepare()
        {
            return LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>().enableBulkLoadTransporters;
        }

        /// <summary>
        /// After a thing is added to the transporter, check if it's a pawn.
        /// If so, scan their PUAH inventory for items still needed by the transport.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(CompTransporter __instance, Thing t)
        {
            // NOTE: 这个功能目前只针对穿梭机
            if (__instance.parent.TryGetComp<CompShuttle>() == null)
            {
                return;
            }

            if (!(t is Pawn pawnEntering) || pawnEntering.inventory?.innerContainer == null || !pawnEntering.inventory.innerContainer.Any)
            {
                return;
            }

            // 引入PUAH状态作为最终的“事实来源”。
            var puahComp = pawnEntering.TryGetComp<CompHauledToInventory>();
            if (puahComp == null) return;
            var puahSet = puahComp.GetHashSet();
            if (puahSet.Count == 0) return;

            if (__instance.leftToLoad.NullOrEmpty())
            {
                return;
            }

            var pawnInventory = pawnEntering.inventory.innerContainer;
            var transporterContainer = __instance.innerContainer;
            var needs = __instance.leftToLoad;

            // 我们只关心那些同时存在于物理背包和PUAH逻辑库存中的物品
            List<Thing> itemsToConsider = pawnInventory.Where(item => puahSet.Contains(item)).ToList();

            foreach (var thingInBackpack in itemsToConsider)
            {
                var bestMatch = BulkLoad_Utility.FindBestMatchFor(thingInBackpack, needs);
                if (bestMatch != null)
                {
                    // 转移任务清单上还需要的数量
                    int countToTransfer = Mathf.Min(thingInBackpack.stackCount, bestMatch.CountToTransfer);

                    if (countToTransfer > 0)
                    {
                        pawnInventory.TryTransferToContainer(thingInBackpack, transporterContainer, countToTransfer, out _);
                    }
                }
            }
        }
    }
}