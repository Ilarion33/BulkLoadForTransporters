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
                //DebugLogger.LogMessage(LogCategory.Toils, () => $"{pawn.LabelShort} is replanning job for '{loadable.GetParentThing().LabelCap}'.");

                //// 在重新规划前，清空任何可能存在的、由玩家手动添加的后续任务队列，确保我们的规划是权威的。
                //pawn.jobs.ClearQueuedJobs(false);

                //CentralLoadManager.Instance?.RegisterOrUpdateTask(loadable);

                //if (WorkGiver_Utility.TryCreateUnloadFirstJob(pawn, loadable, out Job newUnloadJob))
                //{
                //    DebugLogger.LogMessage(LogCategory.Toils, () => "  - Opportunistic unload job found. Converting current job.");
                //    // 如果有，将当前Job转换为一个“仅卸货”Job。
                //    job.targetQueueA = newUnloadJob.targetQueueA;
                //    job.countQueue = newUnloadJob.countQueue;
                //    job.haulOpportunisticDuplicates = newUnloadJob.haulOpportunisticDuplicates;
                //}
                //else
                //{
                //    DebugLogger.LogMessage(LogCategory.Toils, () => "  - No opportunistic unload job. Proceeding with original pickup plan.");
                //    // 信任 WorkGiver 在不久前生成的计划。
                //    job.haulOpportunisticDuplicates = false;
                //}

                //// 检查最终的计划是否有效
                //if (job.targetQueueA == null || !job.targetQueueA.Any())
                //{
                //    DebugLogger.LogMessage(LogCategory.Toils, () => "  - Final plan is empty. Ending job as Succeeded.");
                //    // 如果 WorkGiver 的计划因为时差而失效，或者卸货计划为空，则正常结束任务。
                //    pawn.jobs.EndCurrentJob(JobCondition.Succeeded, true);
                //    return;
                //}

                //DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Final plan has {job.targetQueueA.Count} targets. Claiming items with Manager.");

                // 为最终确定的计划认领资源
                CentralLoadManager.Instance?.ClaimItems(pawn, job, loadable);

            };
            return toil;
        }
    }
}