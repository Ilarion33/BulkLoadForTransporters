// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadTransporters/Toil_PrepareNextUnloadItem.cs
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using System.Linq;
using Verse;
using Verse.AI;

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
                DebugLogger.LogMessage(LogCategory.Toils, () => $"{pawn.LabelShort} is preparing next item to unload.");

                // 优先检查手上是否已经拿着我们需要卸载的物品
                Thing carriedThing = pawn.carryTracker.CarriedThing;
                if (carriedThing != null && haulState.HauledThings.Contains(carriedThing))
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Item '{carriedThing.LabelCap}' is already in hand. Preparing to unload it.");

                    curJob.SetTarget(TargetIndex.C, carriedThing);
                    haulState.RemoveHauledThing(carriedThing);
                    return;
                }

                // 如果手上没有，则从背包里拿一个
                Thing thingToPrepare = haulState.HauledThings
                    .FirstOrDefault(t => t != null && !t.Destroyed && pawn.inventory.innerContainer.Contains(t));

                if (thingToPrepare == null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => "  - No more valid items found in HauledThings list to prepare. Unload loop should end.");
                    return;
                }
                DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Found '{thingToPrepare.LabelCap}' in inventory. Preparing to unload it.");

                // 将找到的物品设为TargetC，并从背包转移到手上。
                curJob.SetTarget(TargetIndex.C, thingToPrepare);
                pawn.inventory.innerContainer.TryTransferToContainer(thingToPrepare, pawn.carryTracker.innerContainer, thingToPrepare.stackCount, out _);

                // 确认转移成功后，再从逻辑账本中移除。
                if (pawn.carryTracker.CarriedThing == thingToPrepare)
                {
                    haulState.RemoveHauledThing(thingToPrepare);
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Successfully moved '{thingToPrepare.LabelCap}' to carry tracker.");
                }
                else
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - FAILED to move '{thingToPrepare.LabelCap}' to carry tracker.");
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}