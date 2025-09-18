// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadTransporters/Toil_TakeFromContainer.cs
using BulkLoadForTransporters.Core.Interfaces;
using RimWorld;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_LoadTransporters
{
    /// <summary>
    /// Creates a Toil that takes an item from a container, drops it on the ground,
    /// and retargets the job to the newly spawned item.
    /// </summary>
    /// <param name="itemIndex">The target index of the item to take (which is currently inside a container).</param>
    /// <param name="toCarryTracker">Legacy parameter, currently unused.</param>
    /// <param name="haulState">The haul state of the current job driver.</param>
    /// <returns>A configured Toil.</returns>
    public static class Toil_DropAndTakeFromContainer
    {
        public static Toil Create(TargetIndex itemIndex, bool toCarryTracker, IBulkHaulState haulState)
        {
            Toil toil = ToilMaker.MakeToil("TakeFromContainer_Hardened");
            toil.initAction = () =>
            {
                Pawn pawn = toil.actor;
                Job job = pawn.CurJob;
                Thing thingToTake = job.GetTarget(itemIndex).Thing;

                // 检查目标物品是否存在，是否已经在地上（不应该由这个Toil处理），以及它是否真的在一个容器里。
                if (thingToTake == null || thingToTake.Destroyed || thingToTake.Spawned || !(thingToTake.ParentHolder is Thing container))
                {
                    // 如果任何条件不满足，说明Job状态已损坏或过时，立即以失败告终。
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                    return;
                }

                var sourceOwner = container.TryGetInnerInteractableThingOwner();
                // 检查容器本身是否有效，以及我们要拿的书是否真的还在里面。
                // 这能处理“书在小人走向书架的途中被别人拿走了”的边缘情况。
                if (sourceOwner == null || !sourceOwner.Contains(thingToTake))
                {
                    // 书不见了，任务无法继续。
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                    return;
                }


                // 动态预定容器，这是解决并发的关键。
                if (!pawn.Reserve(container, job))
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                    return;
                }

                // 从容器中取出物品，丢在地上。
                if (sourceOwner.TryDrop(thingToTake, pawn.Position, pawn.Map, ThingPlaceMode.Near, 1, out Thing droppedThing))
                {
                    // 成功丢出后，立刻释放对容器的预定，让其他小人可以使用它。
                    pawn.Map.reservationManager.Release(container, pawn, job);

                    // 将Job的目标重定向到这个刚掉在地上的物品。
                    job.SetTarget(itemIndex, droppedThing);
                    // 预定这个新目标，为下一步的拾取做准备。
                    pawn.Reserve(droppedThing, job);
                }
                else
                {
                    // 如果因为某些原因（例如，目标位置被阻挡）丢出失败，则任务失败。
                    pawn.Map.reservationManager.Release(container, pawn, job); 
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}