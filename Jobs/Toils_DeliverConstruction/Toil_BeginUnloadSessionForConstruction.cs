// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_DeliverConstruction/Toil_BeginUnloadSessionForConstruction.cs
using BulkLoadForTransporters.Core.Utils;
using BulkLoadForTransporters.Jobs;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_DeliverConstruction
{
    public static class Toil_BeginUnloadSessionForConstruction
    {
        public static Toil Create()
        {
            Toil toil = ToilMaker.MakeToil("BeginUnloadSessionForConstruction");
            toil.initAction = () =>
            {
                var driver = toil.actor.jobs.curDriver as JobDriver_DeliverConstructionInBulk;
                if (driver == null) return;
                DebugLogger.LogMessage(LogCategory.Toils, () => $"{toil.actor.LabelShort} is beginning unload session for Construction Site.");

                var jobTarget = driver.job.targetB.Thing as IConstructible;
                if (jobTarget == null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"-> Toil FAILED: Target '{(driver.job.targetB.Thing)?.LabelCap}' is not an IConstructible.");
                    return;
                }

                var singleTargetNeeds = jobTarget.TotalMaterialCost();
                driver._unloadRemainingNeeds = new Dictionary<ThingDef, int>();

                if (singleTargetNeeds != null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Caching needs from construction site.");
                    foreach (var need in singleTargetNeeds)
                    {
                        if (driver.HauledThings.Any(t => t.def == need.thingDef))
                        {
                            driver._unloadRemainingNeeds[need.thingDef] = jobTarget.ThingCountNeeded(need.thingDef);
                        }
                    }
                }
                driver.SurplusThings.Clear();
                DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Session initialized. {driver._unloadRemainingNeeds.Count} defs with needs cached. Surplus cleared.");
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}