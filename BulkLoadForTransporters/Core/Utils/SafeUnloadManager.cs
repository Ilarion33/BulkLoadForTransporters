// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/SafeUnloadManager.cs
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Core.Utils
{
    /// <summary>
    /// A helper class to ensure the mod can be safely removed from a save game.
    /// </summary>
    public static class SafeUnloadManager
    {
        /// <summary>
        /// Cleans up all custom jobs created by this mod before the game saves.
        /// </summary>
        public static void CleanupBeforeSaving()
        {
            if (Current.Game == null || Find.Maps == null)
            {
                return;
            }

            var settings = LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>();

            // 如果所有功能都已在设置中关闭，则无需进行任何清理操作。
            if (!settings.enableBulkLoadTransporters && !settings.enableBulkLoadPortal && !settings.enableBulkUnloadCarriers)
            {
                return;
            }

            int jobsCleaned = 0;

            // 仅当“批量装载”功能开启时，才清理相关的装载任务。
            if (settings.enableBulkLoadTransporters || settings.enableBulkLoadPortal)
            {
                var manager = CentralLoadManager.Instance;
                if (manager != null)
                {
                    // NOTE: 我们只检查被 CentralLoadManager 追踪的Pawn。
                    HashSet<Pawn> activeWorkers = manager.GetAllActiveWorkers();
                    jobsCleaned += CleanupJobsForPawns(activeWorkers, JobDefRegistry.LoadingJobs);
                }
            }

            // 仅当“批量卸载”功能开启时，才清理相关的卸载任务。
            if (settings.enableBulkUnloadCarriers)
            {
                // NOTE: 卸载任务没有集中的管理器，所以我们必须遍历所有地图的所有Pawn。
                List<Pawn> allPawns = Find.Maps.SelectMany(map => map.mapPawns.AllPawns).ToList();
                jobsCleaned += CleanupJobsForPawns(allPawns, JobDefRegistry.UnloadingJobs);
            }

           
        }

        /// <summary>
        /// A reusable helper method to clean up specific jobs from a given list of pawns.
        /// </summary>
        private static int CleanupJobsForPawns(IEnumerable<Pawn> pawns, HashSet<JobDef> jobDefsToClean)
        {
            int cleanedCount = 0;
            if (jobDefsToClean == null || jobDefsToClean.Count == 0) return 0;

            foreach (var pawn in pawns)
            {
                if (pawn == null || pawn.Destroyed || pawn.jobs == null) continue;

                // 替换当前正在执行的任务。
                if (pawn.CurJob != null && jobDefsToClean.Contains(pawn.CurJob.def))
                {
                    pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wait, 2, true), JobTag.Misc);
                    cleanedCount++;
                }

                if (pawn.jobs.jobQueue != null && pawn.jobs.jobQueue.Count > 0)
                {
                    var originalCount = pawn.jobs.jobQueue.Count;
                    pawn.jobs.jobQueue.RemoveAll(pawn, job => job != null && jobDefsToClean.Contains(job.def));
                    cleanedCount += (originalCount - pawn.jobs.jobQueue.Count);
                }
            }
            return cleanedCount;
        }
    }
}