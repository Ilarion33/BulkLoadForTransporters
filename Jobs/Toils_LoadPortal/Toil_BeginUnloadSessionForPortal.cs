// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadPortal/Toil_BeginUnloadSessionForPortal.cs
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Jobs.Toils_LoadPortal
{
    /// <summary>
    /// A utility class for creating Toils related to starting an unload session for a MapPortal.
    /// </summary>
    public static class Toil_BeginUnloadSessionForPortal
    {
        /// <summary>
        /// Creates a Toil that prepares the JobDriver's state for the portal unloading process.
        /// </summary>
        public static Toil Create()
        {
            Toil toil = ToilMaker.MakeToil("BeginUnloadSessionForPortal");
            toil.initAction = () =>
            {
                var driver = toil.actor.jobs.curDriver as JobDriver_BulkLoadBase;
                if (driver == null) return;

                var jobTarget = driver.job.targetB.Thing;

                // NOTE: 这是此Toil与运输仓版本唯一的、核心的区别。
                // 我们直接将目标转换为MapPortal，而不是尝试获取CompTransporter。
                var currentPortal = jobTarget as MapPortal;
                if (currentPortal == null) return;

                driver._unloadTransferables = currentPortal.leftToLoad;
                driver._unloadRemainingNeeds = new Dictionary<ThingDef, int>();
                if (driver._unloadTransferables != null)
                {
                    foreach (var tr in driver._unloadTransferables.Where(t => t.CountToTransfer > 0 && t.HasAnyThing))
                    {
                        if (driver._unloadRemainingNeeds.ContainsKey(tr.ThingDef))
                            driver._unloadRemainingNeeds[tr.ThingDef] += tr.CountToTransfer;
                        else
                            driver._unloadRemainingNeeds.Add(tr.ThingDef, tr.CountToTransfer);
                    }
                }
                driver.SurplusThings.Clear();
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}