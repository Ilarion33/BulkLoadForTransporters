// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadTransporters/Toil_PrepareNextUnloadItem.cs
using BulkLoadForTransporters.Core.Interfaces;
using Verse;
using Verse.AI;
using System.Linq;

namespace BulkLoadForTransporters.Toils_LoadTransporters
{
    /// <summary>
    /// Prepares the next item for an unload operation by moving it from inventory to the carry tracker.
    /// </summary>
    public static class Toil_PrepareNextUnloadItem
    {
        public static Toil Create()
        {
            Toil toil = ToilMaker.MakeToil("PrepareNextUnloadItem");
            toil.initAction = () =>
            {
                Pawn pawn = toil.actor;
                Job curJob = pawn.CurJob;
                if (curJob == null) return;
                if (!(pawn.jobs.curDriver is IBulkHaulState haulState)) return;

                // 优先检查手上是否已经拿着我们需要卸载的物品
                Thing carriedThing = pawn.carryTracker.CarriedThing;
                if (carriedThing != null && haulState.HauledThings.Contains(carriedThing))
                {
                    curJob.SetTarget(TargetIndex.C, carriedThing);
                    haulState.RemoveHauledThing(carriedThing);
                    return;
                }

                // 如果手上没有，则从背包里拿一个
                Thing thingToPrepare = haulState.HauledThings
                    .FirstOrDefault(t => t != null && !t.Destroyed && pawn.inventory.innerContainer.Contains(t));

                if (thingToPrepare == null)
                {
                    return;
                }

                // 将找到的物品设为TargetC，并从背包转移到手上。
                curJob.SetTarget(TargetIndex.C, thingToPrepare);
                pawn.inventory.innerContainer.TryTransferToContainer(thingToPrepare, pawn.carryTracker.innerContainer, thingToPrepare.stackCount, out _);

                // 确认转移成功后，再从逻辑账本中移除。
                if (pawn.carryTracker.CarriedThing == thingToPrepare)
                {
                    haulState.RemoveHauledThing(thingToPrepare);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}