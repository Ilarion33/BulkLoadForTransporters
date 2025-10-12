// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_DeliverConstruction/Toil_ClearSiteIfNecessary.cs
using BulkLoadForTransporters.Core.Utils;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_DeliverConstruction
{
    /// <summary>
    /// A Toil that performs on-site reconnaissance. If it finds any blockers or
    /// required pre-construction tasks (like removing a floor), it interrupts the
    /// current bulk delivery job and issues a new, high-priority cleanup job.
    /// The "three-part glue" system will then handle resuming the delivery.
    /// </summary>
    public static class Toil_ClearSiteIfNecessary
    {
        public static Toil Create()
        {
            Toil toil = ToilMaker.MakeToil("ClearSiteIfNecessary");
            toil.initAction = () =>
            {
                var pawn = toil.actor;
                var job = pawn.CurJob;
                var target = job.GetTarget(TargetIndex.B).Thing;

                if (target == null || !target.Spawned)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                    return;
                }

                // Call our unified utility to handle all checks.
                if (JobDriver_Utility.TryCreateClearSiteJob(pawn, target, job.playerForced, out Job cleanupJob))
                {
                    // A cleanup job is necessary. Interrupt the current job and start the new one.
                    pawn.jobs.jobQueue.EnqueueFirst(cleanupJob, JobTag.Misc);
                }
                // If no cleanup job is needed, this Toil does nothing and completes instantly.
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}