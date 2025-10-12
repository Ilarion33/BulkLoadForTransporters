// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Utils/Global_Utility.cs
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Jobs;
using PickUpAndHaul;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Core.Utils
{
    public static class Global_Utility
    {
        // NOTE: 这是一个全局信号旗，用来在执行我们自己的卸货记账时，
        public static bool IsExecutingManagedUnload = false;


        /// <summary>
        /// A high-performance, manually replicated version of the core logic from
        /// CaravanFormingUtility.AllReachableColonyItems. It checks if a thing
        /// is considered a valid "colony asset" for loading purposes.
        /// </summary>
        public static bool IsValidColonyAsset(Thing t, Pawn pawn)
        {
            // Must be spawned and not forbidden for the specific pawn
            if (!t.Spawned || t.IsForbidden(pawn)) return false;

            // Must belong to the player faction, if it has a faction
            if (t.Faction != null && t.Faction != Faction.OfPlayer) return false;

            // On non-home maps, area checks are ignored.
            if (!pawn.Map.IsPlayerHome) return true;

            // Check if the item is in a storage zone/slot. This is the highest priority check.
            var slotGroup = t.GetSlotGroup();
            if (slotGroup != null && slotGroup.Settings.AllowedToAccept(t)) return true;

            // Check if the item is in the home area.
            if (pawn.Map.areaManager.Home[t.Position]) return true;

            // If none of the above, it's not a valid colony asset for general pickup.
            return false;
        }

        
        /// <summary>
        /// Checks if a Thing is "unbackpackable" due to its type (e.g., a live Pawn)
        /// or mod settings (e.g., corpses forbidden by Pick Up And Haul).
        /// </summary>
        public static bool IsUnbackpackable(Thing t)
        {
            if (t is Pawn && !(t is Corpse))
            {
                return true;
            }

            // 一个尸体是否可入包，取决于 Pick Up And Haul 的设置。
            if (t is Corpse && !PickUpAndHaul.Settings.AllowCorpses)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// A wrapper method to safely register a collection of things with Pick Up And Haul's inventory system.
        /// </summary>
        public static void RegisterHauledThingsWithPuah(Pawn pawn, IEnumerable<Thing> thingsToRegister)
        {
            if (pawn == null || thingsToRegister == null || !thingsToRegister.Any())
            {
                return;
            }

            var puahComp = pawn.TryGetComp<PickUpAndHaul.CompHauledToInventory>();
            if (puahComp == null)
            {
                return;
            }

            foreach (var thing in thingsToRegister)
            {
                if (thing != null && !thing.Destroyed)
                {
                    puahComp.RegisterHauledItem(thing);
                }
            }
        }
              
        
        /// <summary>
        /// Checks if a thing is "fungible" (interchangeable, like steel) or "non-fungible"
        /// (has unique properties, like quality or hit points on a weapon).
        /// </summary>
        public static bool IsFungible(Thing thing)
        {
            if (thing == null) return true;

            if (thing is MinifiedThing minifiedThing)
            {
                thing = minifiedThing.InnerThing;
                if (thing == null) return true; // 如果内部物品为空，则视为可堆叠（虽然不太可能）
            }

            // 基础检查
            if (thing.def.stackLimit <= 1) return false;
            if (thing.def.tradeNeverStack) return false;

            // 品质
            if (thing.TryGetQuality(out _)) return false;

            // 材料
            if (thing.def.MadeFromStuff) return false;

            // 耐久度
            if (thing.def.useHitPoints && thing.HitPoints < thing.MaxHitPoints) return false;

            // 特殊组件
            if (thing.TryGetComp<CompIngredients>() != null) return false;
            if (thing.TryGetComp<CompArt>() != null) return false;
            if (thing is Genepack || thing is Xenogerm) return false;

            // 武器特殊属性
            var weapon = thing as ThingWithComps;
            if (weapon != null && weapon.TryGetComp<CompBiocodable>()?.Biocoded == true) return false;


            // 服装特殊属性
            var apparel = thing as Apparel;
            if (apparel != null && apparel.WornByCorpse) return false;

            // 活物/尸体
            if (thing is Pawn || thing is Corpse) return false;

            return true;
        }

        
        /// <summary>
        /// A core matching algorithm that finds the best TransferableOneWay entry for a specific Thing instance.
        /// It correctly handles non-fungible items by checking for direct reference first.
        /// </summary>
        public static TransferableOneWay FindBestMatchFor(Thing thing, List<TransferableOneWay> transferables)
        {
            if (transferables == null) return null;

            bool isStackable = thing.def.stackLimit > 1;

            foreach (var tr in transferables)
            {
                if (tr.CountToTransfer > 0 && tr.things.Contains(thing)) return tr;
            }

            if (!isStackable)
            {
                return null;
            }

            var candidates = transferables.Where(t => t.ThingDef == thing.def && t.CountToTransfer > 0).ToList();
            if (!candidates.Any()) return null;

            if (candidates.Count == 1) return candidates[0];

            int countInHand = thing.stackCount;

            var exactMatches = candidates.Where(t => t.CountToTransfer == countInHand).ToList();
            if (exactMatches.Count == 1) return exactMatches[0];
            if (exactMatches.Count > 1) candidates = exactMatches;

            var fulfillingMatches = candidates.Where(t => t.CountToTransfer < countInHand).ToList();
            if (fulfillingMatches.Any())
            {
                return fulfillingMatches.OrderByDescending(t => t.CountToTransfer).First();
            }

            var partialMatches = candidates.Where(t => t.CountToTransfer > countInHand).ToList();
            if (partialMatches.Any())
            {
                return partialMatches.OrderBy(t => t.CountToTransfer).First();
            }

            return candidates.FirstOrDefault();
        }


        /// <summary>
        /// The definitive logic gate to determine if a Pawn is cargo or an autonomous agent for a loading task.
        /// </summary>
        public static bool NeedsToBeCarried(Pawn p)
        {
            if (p.Downed || p.Dead)
            {
                return true;
            }

            // AI接到了“自主登船”命令的单位，会自己走路。
            if (p.mindState?.duty?.def == DutyDefOf.LoadAndEnterTransporters)
            {
                return false;
            }


            // 被系统视为“殖民者”的单位（包括玩家可控的机械体）
            if (p.IsColonist || (p.IsColonyMech && !p.IsSelfShutdown()))
            {
                return false;
            }

            return true;
        }


        /// <summary>
        /// Calculates how many of a surplus item can be stored in a pawn's inventory.
        /// Obeys the global "cheat" setting to ignore mass limits.
        /// </summary>
        public static int CalculateSurplusToStoreInInventory(Pawn pawn, Thing surplus)
        {
            if (LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().cheatIgnoreInventoryMass)
            {
                return surplus.stackCount; // 作弊模式：返回全部数量
            }

            // 正常模式：执行基于负重的精确计算
            float availableMass = MassUtility.FreeSpace(pawn);
            if (availableMass <= 0) return 0;

            float massPerItem = surplus.GetStatValue(StatDefOf.Mass);
            if (massPerItem <= 0) return surplus.stackCount;

            int countToStore = Mathf.FloorToInt(availableMass / massPerItem);

            return Mathf.Min(surplus.stackCount, countToStore);
        }

    }
}