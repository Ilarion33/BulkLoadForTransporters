// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadTransporters/Toil_ReplanJob.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_LoadTransporters
{
    /// <summary>
    /// A static class containing the factory method for the replanning Toil.
    /// </summary>
    public static class Toil_ReplanJob
    {
        /// <summary>
        /// Creates a Toil that re-evaluates the loading situation and builds a new hauling plan.
        /// This includes checking for opportunistic unloads first, then planning a new pickup trip.
        /// </summary>
        /// <param name="loadable">The loading task interface, providing context.</param>
        /// <returns>A configured Toil ready to be used in a JobDriver.</returns>
        public static Toil Create(IManagedLoadable loadable)
        {
            Toil toil = ToilMaker.MakeToil("ReplanJob");
            toil.initAction = () =>
            {
                var pawn = toil.actor;
                var job = pawn.CurJob;

                // 在重新规划前，清空任何可能存在的、由玩家手动添加的后续任务队列，确保我们的规划是权威的。
                pawn.jobs.ClearQueuedJobs(false);
                
                CentralLoadManager.Instance?.RegisterOrUpdateTask(loadable);

                if (BulkLoad_Utility.TryCreateUnloadFirstJob(pawn, loadable, out Job newUnloadJob))
                {
                    // 如果有，将当前Job转换为一个“仅卸货”Job。
                    job.targetQueueA = newUnloadJob.targetQueueA;
                    job.countQueue = newUnloadJob.countQueue;
                    job.haulOpportunisticDuplicates = newUnloadJob.haulOpportunisticDuplicates;
                }
                else
                {
                    // 否则，开始规划一次新的“拾取-卸货”行程。
                    var constraints = CentralLoadManager.Instance?.GetAvailableToClaim(loadable) ?? new Dictionary<ThingDef, int>();
                    var plan = LoadingPlanner.TryCreateHaulPlan(pawn, loadable, constraints);

                    if (plan == null)
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.Succeeded, true);
                        return;
                    }
                    job.targetQueueA = plan.Targets;
                    job.countQueue = plan.Counts;
                    job.haulOpportunisticDuplicates = false;
                }

                // 如果制定出了任何需要拾取的计划。
                if (job.targetQueueA != null && job.targetQueueA.Any())
                {
                    CentralLoadManager.Instance?.ClaimItems(pawn, job, loadable);
                }

            };
            return toil;
        }
    }
}