// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadTransporters/Toil_EndUnloadSession.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using BulkLoadForTransporters.Jobs;
using RimWorld;
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

                // 卸货会话的常规清理。
                driver._unloadTransferables = null;
                driver._unloadRemainingNeeds = null;

                if (driver.job.loadID != 1) return;

                // 检查功能是否在设置中启用
                var settings = LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>();
                if (!settings.enableContinuousLoading) return;

                Pawn pawn = driver.pawn;

                // 半径被硬编码为10
                const float scanRadius = 10f;
                float scanRadiusSquared = scanRadius * scanRadius;

                // 先进行廉价的距离和派系过滤，再对少数候选者执行昂贵的HasPotentialBulkWork检查
                var nearbyTransporters = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Transporter)
                    .Select(t => t.TryGetComp<CompTransporter>())
                    .Where(tr => tr != null &&
                                 tr.parent.Faction == pawn.Faction && 
                                 tr.parent.Position.DistanceToSquared(pawn.Position) <= scanRadiusSquared)
                    .OrderBy(tr => pawn.Position.DistanceToSquared(tr.parent.Position))
                    .ToList(); 

                CompTransporter bestNextTransporter = null;
                foreach (var tr in nearbyTransporters)
                {
                    // HACK: 为了调用通用的 Utility 方法，我们在这里为每个候选者都创建了一个临时的 Adapter。
                    IManagedLoadable groupLoadable = new LoadTransportersAdapter(tr);
                    if (LoadTransporters_WorkGiverUtility.HasPotentialBulkWork(pawn, groupLoadable))
                    {
                        bestNextTransporter = tr;
                        break;
                    }
                }

                if (bestNextTransporter != null)
                {
                    IManagedLoadable groupLoadable = new LoadTransportersAdapter(bestNextTransporter);
                    if (LoadTransporters_WorkGiverUtility.TryGiveBulkJob(pawn, groupLoadable, out Job nextJob) && nextJob.def != JobDefOf.Wait)
                    {
                        // 将“连续模式”标记传递给下一个Job，以实现循环。
                        nextJob.loadID = 1;
                        pawn.jobs.TryTakeOrderedJob(nextJob, JobTag.Misc);
                    }
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}