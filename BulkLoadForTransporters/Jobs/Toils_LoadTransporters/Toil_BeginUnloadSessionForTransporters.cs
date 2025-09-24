// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadTransporters/Toil_BeginUnloadSession.cs
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
    /// A Toil that initializes the session state for an unloading sequence.
    /// It captures a snapshot of the current target's needs, allowing for consistent checks
    /// throughout the multi-Toil unloading process.
    /// </summary>
    public static class Toil_BeginUnloadSessionForTransporters
    {
        /// <summary>
        /// Creates the Toil for initializing the unload session.
        /// </summary>
        /// <returns>A configured Toil instance.</returns>
        public static Toil Create()
        {
            Toil toil = ToilMaker.MakeToil("BeginUnloadSession");
            toil.initAction = () =>
            {
                var driver = toil.actor.jobs.curDriver as JobDriver_LoadTransportersInBulk;
                if (driver == null) return;
                DebugLogger.LogMessage(LogCategory.Toils, () => $"{toil.actor.LabelShort} is beginning unload session for Transporter.");


                var jobTarget = driver.job.targetB.Thing;
                var currentTransporter = jobTarget?.TryGetComp<CompTransporter>();
                if (currentTransporter == null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"-> Toil FAILED: Target '{jobTarget?.LabelCap}' has no CompTransporter.");
                    return;
                }

                // 获取目标“个体”的待办清单，缩小后续检查的视野。
                driver._unloadTransferables = currentTransporter.leftToLoad;
                driver._unloadRemainingNeeds = new Dictionary<ThingDef, int>();
                if (driver._unloadTransferables != null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Caching {driver._unloadTransferables.Count} transferables from Transporter.");

                    // 将待办清单处理成一个易于查询的字典，用于快速计算剩余需求。
                    foreach (var tr in driver._unloadTransferables.Where(t => t.CountToTransfer > 0 && t.HasAnyThing))
                    {
                        if (driver._unloadRemainingNeeds.ContainsKey(tr.ThingDef))
                            driver._unloadRemainingNeeds[tr.ThingDef] += tr.CountToTransfer;
                        else
                            driver._unloadRemainingNeeds.Add(tr.ThingDef, tr.CountToTransfer);
                    }
                }
                // 清空上一趟可能产生的剩余物列表，为新的卸货循环做准备。
                driver.SurplusThings.Clear();
                DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Session initialized. {driver._unloadRemainingNeeds.Count} defs with needs cached. Surplus cleared.");
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}