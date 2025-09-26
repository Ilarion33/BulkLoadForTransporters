// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Utils/BulkLoad_Utility.cs
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
    public static class BulkLoad_Utility
    {
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

        // NOTE: 这是一个全局信号旗，用来在执行我们自己的卸货记账时，
        public static bool IsExecutingManagedUnload = false;

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
        /// Validates if the pawn's carried items are still needed by a single, non-grouped target (e.g., a Portal).
        /// This version does not perform complex redirection.
        /// </summary>
        public static bool ValidateSingleTarget(JobDriver_BulkLoadBase driver, IManagedLoadable loadable)
        {
            var pawn = driver.pawn;
            var job = driver.job;

            var carriedThings = driver.HauledThings
                .Where(t => t != null && !t.Destroyed && (pawn.inventory.innerContainer.Contains(t) || pawn.carryTracker.CarriedThing == t))
                .ToList();

            if (!carriedThings.Any())
            {
                return true;
            }

            var individualNeeds = loadable.GetTransferables();
            if (individualNeeds == null)
            {
                return false;
            }

            bool isTargetStillValid = carriedThings.Any(carried => FindBestMatchFor(carried, individualNeeds) != null);

            return isTargetStillValid;
        }

        /// <summary>
        /// A powerful validation tool for grouped targets (e.g., Transporters).
        /// If the current target no longer needs the carried items, it automatically finds the next valid
        /// target within the group and redirects the pawn's path.
        /// </summary>
        public static bool ValidateAndRedirectCurrentTarget(JobDriver_LoadTransportersInBulk driver)
        {
            var pawn = driver.pawn;
            var job = driver.job;

            var carriedThing = pawn.carryTracker.CarriedThing;
            Thing priorityThing = null;

            // 如果手上拿着一个Pawn，它就是我们的最高优先级目标
            if (carriedThing != null && carriedThing is Pawn)
            {
                priorityThing = carriedThing;
            }

            List<Thing> thingsToValidate;
            if (priorityThing != null)
            {
                thingsToValidate = new List<Thing> { priorityThing };
            }
            else
            {
                thingsToValidate = driver.HauledThings
                    .Where(t => t != null && !t.Destroyed && (pawn.inventory.innerContainer.Contains(t) || t == carriedThing))
                    .ToList();
            }

            if (!thingsToValidate.Any())
            {
                return true; 
            }

            var currentTargetThing = job.targetB.Thing;
            if (currentTargetThing == null || currentTargetThing.Destroyed) return false;

            var currentTransporter = currentTargetThing.TryGetComp<CompTransporter>();
            if (currentTransporter == null) return false;

            var individualNeeds = currentTransporter.leftToLoad;
            bool isCurrentTargetValid = individualNeeds != null &&
                                      thingsToValidate.Any(thing => FindBestMatchFor(thing, individualNeeds) != null);

            if (isCurrentTargetValid)
            {
                return true; 
            }

            // 如果当前目标无效，则在整个组内“寻址”
            var groupTransporters = currentTransporter.TransportersInGroup(pawn.Map);
            if (groupTransporters == null) return false;

            Thing newTarget = null;
            foreach (var otherTransporter in groupTransporters)
            {
                if (otherTransporter.parent == currentTargetThing) continue;

                var otherNeeds = otherTransporter.leftToLoad;
                if (otherNeeds != null && thingsToValidate.Any(thing => FindBestMatchFor(thing, otherNeeds) != null))
                {
                    newTarget = otherTransporter.parent;
                    break;
                }
            }

            if (newTarget != null)
            {
                if (!pawn.CanReach(newTarget, PathEndMode.Touch, Danger.Deadly))
                {
                    return false;
                }

                job.targetB = new LocalTargetInfo(newTarget);
                pawn.pather.StartPath(job.targetB, PathEndMode.Touch);
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if a thing is "fungible" (interchangeable, like steel) or "non-fungible"
        /// (has unique properties, like quality or hit points on a weapon).
        /// </summary>
        public static bool IsFungible(Thing thing)
        {
            if (thing == null) return true;

            // 基础检查
            //if (thing.def.stackLimit <= 1) return false;
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

        // 这是一个内部辅助方法，用来检查一个物品列表（thingsToCheck）和
        // 一个需求列表（loadable）之间是否存在任何交集。
        private static bool HasAnyNeededItems(IEnumerable<Thing> thingsToCheck, ILoadable loadable)
        {
            if (thingsToCheck == null || !thingsToCheck.Any())
            {
                return false;
            }

            var transferables = loadable.GetTransferables();
            if (transferables == null)
            {
                return false;
            }

            foreach (var thing in thingsToCheck)
            {
                if (thing != null && !thing.Destroyed && FindBestMatchFor(thing, transferables) != null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if at least one of the items currently being hauled for our job is still needed by the target.
        /// </summary>
        public static bool IsCarryingAnythingNeeded(Pawn pawn, ILoadable loadable)
        {
            if (!(pawn.jobs.curDriver is IBulkHaulState haulState))
            {
                return false;
            }
            return HasAnyNeededItems(haulState.HauledThings, loadable);
        }

        /// <summary>
        /// Checks if the pawn has anything in their Pick Up And Haul inventory that is needed by the target.
        /// </summary>
        public static bool PawnHasNeededPuahItems(Pawn pawn, ILoadable loadable)
        {
            var puahComp = pawn.TryGetComp<CompHauledToInventory>();
            return HasAnyNeededItems(puahComp?.GetHashSet(), loadable);
        }


        // 这是一个内部的Job构建器，负责为“仅卸货”模式创建Job。
        // 它可以通过IManagedLoadable接口为任何兼容的目标服务。
        private static Job CreateUnloadJobForTarget(Pawn pawn, IManagedLoadable loadable, IEnumerable<Thing> itemsInBag)
        {
            Job job = JobMaker.MakeJob(JobDefRegistry.GetJobDefFor(loadable.GetParentThing().def));
            job.targetB = new LocalTargetInfo(loadable.GetParentThing());
            job.haulOpportunisticDuplicates = true;

            job.targetQueueA = new List<LocalTargetInfo>();
            job.countQueue = new List<int>();

            var needs = loadable.GetTransferables();
            if (needs == null) return null; 

            foreach (var item in itemsInBag)
            {
                if (item != null && !item.Destroyed && FindBestMatchFor(item, needs) != null)
                {
                    job.targetQueueA.Add(new LocalTargetInfo(item));
                    job.countQueue.Add(item.stackCount);
                }
            }
            return job;
        }


        /// <summary>
        /// Tries to create an "unload only" job for a **specifically designated** target,
        /// typically one that the player has right-clicked.
        /// </summary>
        public static bool TryCreateDirectedUnloadJob(Pawn pawn, IManagedLoadable designatedTarget, out Job job)
        {
            job = null;
            if (!PawnHasNeededPuahItems(pawn, designatedTarget))
            {
                return false;
            }


            var itemsInBag = pawn.TryGetComp<CompHauledToInventory>().GetHashSet();
            job = CreateUnloadJobForTarget(pawn, designatedTarget, itemsInBag);

            return job != null && job.targetQueueA.Any();
        }






        /// <summary>
        /// 定义了一个委托，用于扫描特定类型的可装载目标。
        /// 这个委托由主模块定义，由可选的兼容性模块来实现和注册。
        /// </summary>
        /// <param name="pawn">执行扫描的小人。</param>
        /// <returns>一个 Thing 列表，代表地图上所有潜在的目标。</returns>
        public delegate IEnumerable<Thing> OpportunisticTargetScanner(Pawn pawn);

        /// <summary>
        /// 一个公共的委托列表，允许兼容性模块注册它们自己的目标扫描器。
        /// </summary>
        public static readonly List<OpportunisticTargetScanner> OpportunisticTargetScanners = new List<OpportunisticTargetScanner>();

        /// <summary>
        /// 定义了一个委托，用于将一个 Thing 转换为 IManagedLoadable 接口。
        /// </summary>
        /// <param name="thing">要转换的 Thing。</param>
        /// <returns>一个 IManagedLoadable 实例，如果可以转换的话；否则为 null。</returns>
        public delegate IManagedLoadable AdapterFactory(Thing thing);

        /// <summary>
        /// 一个公共的委托列表，允许兼容性模块注册它们自己的 Adapter 工厂。
        /// </summary>
        public static readonly List<AdapterFactory> AdapterFactories = new List<AdapterFactory>();


        // 静态构造函数，用于注册主模块自己的扫描器和工厂
        static BulkLoad_Utility()
        {
            // 注册原版运输仓和传送门的扫描器
            OpportunisticTargetScanners.Add(pawn => pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Transporter));
            OpportunisticTargetScanners.Add(pawn => pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.MapPortal));

            // 注册原版运输仓和传送门的 Adapter 工厂
            AdapterFactories.Add(t => t.TryGetComp<CompTransporter>() is CompTransporter comp ? new LoadTransportersAdapter(comp) : null);
            AdapterFactories.Add(t => t is MapPortal portal ? new MapPortalAdapter(portal) : null);
        }


        /// <summary>
        /// Tries to create an "unload only" job by opportunistically scanning for the **best nearby** target.
        /// </summary>
        public static bool TryCreateUnloadFirstJob(Pawn pawn, IManagedLoadable loadable, out Job job)
        {
            job = null;
            if (!PawnHasNeededPuahItems(pawn, loadable))
            {
                return false;
            }

            var itemsInBag = pawn.TryGetComp<PickUpAndHaul.CompHauledToInventory>().GetHashSet();
            IManagedLoadable bestTargetLoadable = null;


            // 扫描半径设置
            var settings = LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>();
            float scanRadiusSquared = settings.opportunityScanRadius * settings.opportunityScanRadius;

            // 统一扫描所有潜在的可装载目标类型
            var allPossibleTargets = new List<Thing>();
            foreach (var scanner in OpportunisticTargetScanners)
            {
                allPossibleTargets.AddRange(scanner(pawn));
            }
            // 去重，以防万一
            allPossibleTargets = allPossibleTargets.Distinct().ToList();

            foreach (var itemInBag in itemsInBag)
            {
                if (itemInBag == null || itemInBag.Destroyed) continue;

                var foundLoadable = allPossibleTargets
                    .Select(t => {
                        IManagedLoadable adapter = null;
                        // 关键修改：遍历所有已注册的工厂，尝试创建 Adapter
                        foreach (var factory in AdapterFactories)
                        {
                            adapter = factory(t);
                            if (adapter != null) break; // 第一个成功的工厂就够了
                        }
                        return adapter;
                    })

                    .Where(adapter =>
                        adapter != null &&
                        adapter.GetParentThing().Position.DistanceToSquared(pawn.Position) <= scanRadiusSquared &&
                        adapter.GetTransferables().Any(tr => tr.CountToTransfer > 0) &&
                        FindBestMatchFor(itemInBag, adapter.GetTransferables()) != null)

                    .OrderBy(adapter => pawn.Position.DistanceToSquared(adapter.GetParentThing().Position))
                    .FirstOrDefault();

                if (foundLoadable != null)
                {
                    bestTargetLoadable = foundLoadable;
                    break; // 找到第一个匹配后就跳出循环
                }
            }

            if (bestTargetLoadable != null)
            {
                job = CreateUnloadJobForTarget(pawn, bestTargetLoadable, itemsInBag);
            }

            return job != null && job.targetQueueA.Any();
        }

        /// <summary>
        /// Creates a delegate (Action) that will be executed at the end of a job (in a FinishAction).
        /// It's responsible for the final synchronization of our internal item state with Pick Up And Haul's system.
        /// </summary>
        public static Action<JobCondition> CreatePuahReconciliationAction(Pawn pawn, IBulkHaulState haulState, List<Thing> originalPuahItems)
        {
            return (jobCondition) =>
            {
                var puahComp = pawn.TryGetComp<CompHauledToInventory>();
                if (puahComp == null) return;
                var puahSet = puahComp.GetHashSet();

                foreach (var originalThing in originalPuahItems)
                {
                    puahSet.Remove(originalThing);
                }

                foreach (var surplusThing in haulState.SurplusThings)
                {
                    if (surplusThing != null && !surplusThing.Destroyed)
                    {
                        puahComp.RegisterHauledItem(surplusThing);
                    }
                }

                foreach (var hauledThing in haulState.HauledThings)
                {
                    if (hauledThing != null && !hauledThing.Destroyed)
                    {
                        puahComp.RegisterHauledItem(hauledThing);
                    }
                }
            };
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
        /// Resolves the precise number of an item still needed during an unload session.
        /// This is crucial for preventing over-loading if the player alters the manifest mid-job.
        /// It checks against both the original transferable and the session's remaining needs budget.
        /// </summary>
        /// <param name="thingToUnload">The specific Thing instance being considered for unloading.</param>
        /// <param name="allTransferables">The complete list of transferables for the entire loading task.</param>
        /// <param name="unloadSessionNeeds">A dictionary representing the remaining "budget" of needs for the current pawn's unload trip.</param>
        /// <returns>The precise number of items to deposit.</returns>
        public static int ResolveNeededAmountForUnload(Thing thingToUnload, List<TransferableOneWay> allTransferables, Dictionary<ThingDef, int> unloadSessionNeeds)
        {
            if (allTransferables == null || thingToUnload == null || unloadSessionNeeds == null) return 0;

            // 首先，检查本趟卸货的“预算”中，是否还需要这种类型的物品。
            if (!unloadSessionNeeds.TryGetValue(thingToUnload.def, out int totalNeeded) || totalNeeded <= 0) return 0;

            // 其次，找到与当前物品最匹配的那个原始需求条目。
            var bestMatch = FindBestMatchFor(thingToUnload, allTransferables);

            // 返回两者中的最小值，确保既不超过总需求，也不超过本趟预算。
            if (bestMatch != null)
            {
                return Mathf.Min(bestMatch.CountToTransfer, totalNeeded);
            }

            return 0;
        }

    }
}