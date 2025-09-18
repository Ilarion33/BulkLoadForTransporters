// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadTransporters/Toil_PrepareToUnloadFromInventory.cs
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using PickUpAndHaul;
using RimWorld;
using System.Linq;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_LoadTransporters
{
    /// <summary>
    /// Prepares a bulk unload job by populating the job's internal tracking list (HauledThings)
    /// with relevant items from the pawn's Pick Up And Haul inventory.
    /// </summary>
    public static class Toil_PrepareToUnloadFromInventory
    {
        /// <summary>
        /// Creates a Toil that initializes the unload-only mode.
        /// </summary>
        /// <param name="haulState">The driver's state tracker.</param>
        /// <param name="loadable">The loading destination, used to check which items are needed.</param>
        public static Toil Create(IBulkHaulState haulState, ILoadable loadable)
        {
            Toil toil = ToilMaker.MakeToil("PrepareToUnloadFromInventory");
            toil.initAction = () =>
            {
                var pawn = toil.actor;
                // 获取PUAH组件，这是所有物品的来源。
                var puahComp = pawn.TryGetComp<CompHauledToInventory>();
                if (puahComp == null) return;

                // 获取当前任务的需求清单。
                var neededTransferables = loadable.GetTransferables();
                if (neededTransferables == null) return;

                // 遍历PUAH背包中的所有物品。
                var itemsFromPuah = puahComp.GetHashSet().ToList();
                foreach (var thing in itemsFromPuah)
                {
                    if (BulkLoad_Utility.FindBestMatchFor(thing, neededTransferables) != null)
                    {
                        // TrackOriginalPuahItem 用于在Job结束时正确地与PUAH进行状态同步。
                        haulState.TrackOriginalPuahItem(thing);
                        haulState.AddHauledThing(thing);
                    }
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}