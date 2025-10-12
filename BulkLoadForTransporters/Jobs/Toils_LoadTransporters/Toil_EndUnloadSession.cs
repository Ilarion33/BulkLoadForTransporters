// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadTransporters/Toil_EndUnloadSession.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using BulkLoadForTransporters.Jobs;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_LoadTransporters
{
    /// <summary>
    /// A Toil that finalizes the unloading process and potentially chains to a new loading job.
    /// </summary>
    public static class Toil_EndUnloadSession
    {
        public static Toil Create()
        {
            Toil toil = ToilMaker.MakeToil("EndUnloadSession_And_Chain");
            toil.initAction = () =>
            {
                var driver = toil.actor.jobs.curDriver as JobDriver_LoadTransportersInBulk;
                if (driver == null) return;
                DebugLogger.LogMessage(LogCategory.Toils, () => $"{toil.actor.LabelShort} is ending unload session.");

                driver.ReconcileStateWithPuah(JobCondition.Succeeded);

                // 卸货会话的常规清理。
                driver._unloadTransferables = null;
                driver._unloadRemainingNeeds = null;

                if (driver.job.loadID != 1)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => "  - Not in continuous mode. Ending job.");
                    return;
                }

                // 检查功能是否在设置中启用
                var settings = LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>();
                if (!settings.enableContinuousLoading)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => "  - Continuous mode disabled in settings. Ending job.");
                    return;
                }

                Pawn pawn = driver.pawn;
                // 手动释放当前认领
                //CentralLoadManager.Instance.ReleaseClaimsForPawn(pawn);

                // 半径被硬编码为10
                const float scanRadius = 10f;
                float scanRadiusSquared = scanRadius * scanRadius;
                DebugLogger.LogMessage(LogCategory.Toils, () => $"    - Scan radius: {scanRadius}.");

                // 先进行廉价的距离和派系过滤，再对少数候选者执行昂贵的HasPotentialBulkWork检查
                var nearbyTransporters = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Transporter)
                    .Select(t => t.TryGetComp<CompTransporter>())
                    .Where(tr => tr != null &&
                                 tr.parent.Faction == pawn.Faction && 
                                 tr.parent.Position.DistanceToSquared(pawn.Position) <= scanRadiusSquared)
                    .OrderBy(tr => pawn.Position.DistanceToSquared(tr.parent.Position))
                    .ToList();

                var evaluatedGroupIDs = new HashSet<int>();
                CompTransporter bestNextTransporter = null;

                foreach (var tr in nearbyTransporters)
                {
                    // 获取组ID并检查缓存
                    int groupID = tr.groupID;
                    if (evaluatedGroupIDs.Contains(groupID))
                    {
                        // 这个运输仓所属的组已经被评估过，直接跳过。
                        continue;
                    }

                    // 将此组标记为已评估
                    evaluatedGroupIDs.Add(groupID);

                    IManagedLoadable groupLoadable = new LoadTransportersAdapter(tr);
                    if (WorkGiver_Utility.HasPotentialBulkWork(pawn, groupLoadable))
                    {
                        bestNextTransporter = tr;
                        break;
                    }
                }


                if (bestNextTransporter != null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Found next target: '{bestNextTransporter.parent.LabelCap}'. Attempting to create and chain job.");
                    IManagedLoadable groupLoadable = new LoadTransportersAdapter(bestNextTransporter);
                    if (WorkGiver_Utility.TryGiveBulkJob(pawn, groupLoadable, out Job nextJob) && nextJob.def != JobDefOf.Wait)
                    {
                        // 将“连续模式”标记传递给下一个Job，以实现循环。
                        nextJob.loadID = 1;
                        pawn.jobs.jobQueue.EnqueueFirst(nextJob, JobTag.Misc);
                        DebugLogger.LogMessage(LogCategory.Toils, () => $"    - Successfully created and chained new job: {nextJob.def.defName}.");
                    }
                    else
                    {
                        DebugLogger.LogMessage(LogCategory.Toils, () => $"    - TryGiveBulkJob failed to create a valid job. Ending continuous chain.");
                    }
                }
                else
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => "  - No nearby transporters with work found. Ending continuous chain.");
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}