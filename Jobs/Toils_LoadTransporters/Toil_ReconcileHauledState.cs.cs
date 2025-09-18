// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadTransporters/Toil_ReconcileHauledState.cs
using BulkLoadForTransporters.Core.Interfaces;
using System.Linq;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_LoadTransporters
{
    /// <summary>
    /// A utility Toil that reconciles the logical list of hauled things (IBulkHaulState.HauledThings)
    /// with the pawn's physical inventory and carry tracker. It removes any "ghost" items
    /// from the list that are no longer physically present.
    /// </summary>
    public static class Toil_ReconcileHauledState
    {
        /// <summary>
        /// Creates a Toil that performs a state reconciliation.
        /// </summary>
        /// <param name="haulState">The driver's state tracker to reconcile.</param>
        public static Toil Create(IBulkHaulState haulState)
        {
            Toil toil = ToilMaker.MakeToil("ReconcileHauledState");
            toil.initAction = () =>
            {
                var pawn = toil.actor;
                var hauledThings = haulState.HauledThings;

                // NOTE: 必须创建一个列表副本进行遍历，因为我们将在循环中修改原始列表。
                var itemsToCheck = hauledThings.ToList();

                foreach (var thing in itemsToCheck)
                {
                    // 如果一个物品在逻辑上存在，但在物理上既不在背包也不在手上，那么它就是一个“幽灵”。
                    if (thing == null || thing.Destroyed ||
                        (!pawn.inventory.innerContainer.Contains(thing) && pawn.carryTracker.CarriedThing != thing))
                    {
                        // 从逻辑账本中移除这个“幽灵”，以防止后续卸货逻辑出错。
                        haulState.RemoveHauledThing(thing);
                    }
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}