// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_UnloadCarriers/Toil_FinalizeUnload.cs
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using RimWorld;
using System.Linq;
using Verse;
using Verse.AI;
using System.Collections.Generic;

namespace BulkLoadForTransporters.Toils_UnloadCarriers
{
    /// <summary>
    /// A utility class for creating the finalization Toil for carrier unloading.
    /// </summary>
    public static class Toil_FinalizeUnload
    {
        /// <summary>
        /// Creates a Toil that finalizes the unload job.
        /// </summary>
        public static Toil Create()
        {
            Toil toil = ToilMaker.MakeToil("FinalizeUnloadAndChainJob_Validated");
            toil.initAction = () =>
            {
                Pawn pawn = toil.actor;
                if (!(pawn.jobs.curDriver is IBulkHaulState haulState)) return;

                // HACK: 这是“零信任”验证。我们不信任HauledThings列表的准确性，
                // 而是通过物理检查来创建一个确凿无疑的“实际持有物品”列表。
                var physicallyPresentThings = new List<Thing>();
                foreach (var thing in haulState.HauledThings)
                {
                    if (thing != null && !thing.Destroyed &&
                        (pawn.inventory.innerContainer.Contains(thing) || pawn.carryTracker.CarriedThing == thing))
                    {
                        physicallyPresentThings.Add(thing);
                    }
                }

                // 从这里开始，我们只使用经过物理验证的列表
                var backpackThings = physicallyPresentThings.Where(t => pawn.inventory.innerContainer.Contains(t));
                BulkLoad_Utility.RegisterHauledThingsWithPuah(pawn, backpackThings);

                Thing carriedThing = physicallyPresentThings.FirstOrDefault(t => pawn.carryTracker.CarriedThing == t);
                if (carriedThing == null)
                {
                    return;
                }

                // NOTE: 这是实现无缝工作流的关键。我们不让Job简单结束，
                // 而是主动为手上的物品创建一个新的、标准的Haul Job并立即开始。
                Job haulJob = null;
                if (StoreUtility.TryFindBestBetterStoreCellFor(carriedThing, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out IntVec3 storeCell))
                {
                    haulJob = HaulAIUtility.HaulToCellStorageJob(pawn, carriedThing, storeCell, false);
                }
                else
                {
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                    return;
                }

                if (haulJob != null)
                {
                    pawn.jobs.TryTakeOrderedJob(haulJob, JobTag.Misc);
                }
                // 在成功处理完所有事情后，清空逻辑列表。
                haulState.HauledThings.Clear();
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}