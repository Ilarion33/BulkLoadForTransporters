// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadPortal/Toil_BeginUnloadSession.cs
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using BulkLoadForTransporters.Jobs;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_LoadPortal
{
    /// <summary>
    /// A universal Toil that prepares a JobDriver's state for an unloading process for ANY IManagedLoadable target.
    /// It uses the interface to fetch the current list of items to load, making it target-agnostic.
    /// </summary>
    public static class Toil_BeginUnloadSession
    {
        /// <summary>
        /// Creates a Toil that initializes the session state for the unloading process.
        /// </summary>
        /// <param name="managedLoadable">The loading task, abstracted via the interface.</param>
        public static Toil Create(IManagedLoadable managedLoadable)
        {
            Toil toil = ToilMaker.MakeToil("BeginUnloadSession");
            toil.initAction = () =>
            {
                var driver = toil.actor.jobs.curDriver as JobDriver_BulkLoadBase;
                if (driver == null) return;

                var parentThing = managedLoadable.GetParentThing();
                DebugLogger.LogMessage(LogCategory.Toils, () => $"{toil.actor.LabelShort} is beginning unload session for '{parentThing?.LabelCap}'.");

                // 关键重构：不再对目标进行类型转换，而是通过 IManagedLoadable 接口获取权威的需求清单。
                // 这使得此 Toil 对运输仓、传送门、载具等所有实现该接口的目标完全通用。
                driver._unloadTransferables = managedLoadable.GetTransferables();

                driver._unloadRemainingNeeds = new Dictionary<ThingDef, int>();
                if (driver._unloadTransferables != null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Caching {driver._unloadTransferables.Count} transferables from the loadable target.");
                    foreach (var tr in driver._unloadTransferables.Where(t => t.CountToTransfer > 0 && t.HasAnyThing))
                    {
                        if (driver._unloadRemainingNeeds.ContainsKey(tr.ThingDef))
                            driver._unloadRemainingNeeds[tr.ThingDef] += tr.CountToTransfer;
                        else
                            driver._unloadRemainingNeeds.Add(tr.ThingDef, tr.CountToTransfer);
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