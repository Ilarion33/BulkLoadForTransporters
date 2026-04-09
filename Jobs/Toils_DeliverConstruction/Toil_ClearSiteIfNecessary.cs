// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_DeliverConstruction/Toil_ClearSiteIfNecessary.cs
using BulkLoadForTransporters.Core.Utils;
using Verse;
using Verse.AI;
using RimWorld;

namespace BulkLoadForTransporters.Toils_DeliverConstruction
{
    public static class Toil_ClearSiteIfNecessary
    {
        private const int MaxWaitTicks = 600;

        public static Toil Create()
        {
            Toil toil = ToilMaker.MakeToil("ClearSiteIfNecessary_WithWaiting");

            toil.initAction = () =>
            {
                toil.actor.jobs.curDriver.ticksLeftThisToil = MaxWaitTicks;
            };

            toil.tickAction = () =>
            {
                var pawn = toil.actor;
                var job = pawn.CurJob;
                var target = job.GetTarget(TargetIndex.B).Thing;

                if (target == null || !target.Spawned)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                // 调用清理工具
                if (JobDriver_Utility.TryCreateClearSiteJob(pawn, target, job.playerForced, out Job cleanupJob))
                {
                    pawn.jobs.jobQueue.EnqueueFirst(cleanupJob, JobTag.Misc);

                    toil.actor.jobs.EndCurrentJob(JobCondition.Succeeded, true, true);
                    return;
                }
                else
                {
                    // 是否还有 Pawn 挡路？
                    Thing blockingThing = GenConstruct.FirstBlockingThing(target, pawn);
                    if (blockingThing != null)
                    {
                        Pawn blockingPawn = blockingThing as Pawn;
                        if (blockingPawn != null)
                        {
                            if (blockingPawn.Downed || blockingPawn.Drafted || blockingPawn.jobs.curDriver.asleep)
                            {
                                // 这个 Pawn 在睡觉，等待是无意义的。
                                pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                                return;
                            }
                            else
                            {
                                return; // 继续等待
                            }
                        }
                    }

                    // 没有 Pawn 挡路，现场完全干净，可以继续了。
                    toil.actor.jobs.curDriver.ReadyForNextToil();
                }
            };

            toil.defaultCompleteMode = ToilCompleteMode.Delay;
            toil.FailOnCannotTouch(TargetIndex.B, PathEndMode.Touch);

            return toil;
        }
    }
}