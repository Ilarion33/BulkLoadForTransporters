// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Components/CompBulkLoadablePortal.cs
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using PickUpAndHaul;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Core.Components
{
    /// <summary>
    /// The CompProperties class required by ThingDefs to use our CompBulkLoadablePortal.
    /// </summary>
    public class CompProperties_BulkLoadablePortal : CompProperties
    {
        public CompProperties_BulkLoadablePortal()
        {
            this.compClass = typeof(CompBulkLoadablePortal);
        }
    }

    /// <summary>
    /// Provides the "Prioritize bulk loading" right-click options for MapPortals.
    /// This is a mirrored implementation of CompBulkLoadable.
    /// </summary>
    public class CompBulkLoadablePortal : ThingComp
    {
        private int _cacheTick = -1;
        private const int CacheDurationTicks = 60;
        private Pawn _cachePawn = null;
        private readonly List<FloatMenuOption> _cachedOptions = new List<FloatMenuOption>();

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
        {
            // --- 缓存检查 ---
            if (GenTicks.TicksGame < _cacheTick + CacheDurationTicks && pawn == _cachePawn)
            {
                foreach (var option in _cachedOptions)
                {
                    yield return option;
                }
                yield break;
            }

            _cachedOptions.Clear();
            _cacheTick = GenTicks.TicksGame;
            _cachePawn = pawn;

            DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"Evaluating right-click options for {pawn.LabelShort} on Portal '{parent.LabelCap}' at {parent.Position}?");

            // NOTE: 这是Portal功能的总开关。
            if (!LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkLoadPortal)
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Bulk Load Portals feature is disabled in settings.");
                yield break;
            }

            // 所有前置检查
            if (pawn.Drafted)
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Pawn is drafted.");
                yield break;
            }

            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Pawn is incapable of manipulation.");
                yield break;
            }

            if (pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.Hauling) == 0)
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Hauling work type is disabled for this pawn.");
                yield break;
            }

            if (!(pawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Deadly)))
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Pawn cannot reserve and reach the target.");
                yield break;
            }

            var portal = parent as MapPortal;
            if (portal == null)
            {
                yield break;
            }

            if (pawn.CurJob != null && pawn.CurJob.def == JobDefRegistry.LoadPortal)
            {
                var jobTarget = pawn.CurJob.targetB.Thing as MapPortal;
                if (jobTarget != null && jobTarget.thingIDNumber == portal.thingIDNumber)
                {
                    yield break;
                }
            }

            if (pawn.RaceProps.IsMechanoid && !PickUpAndHaul.Settings.AllowMechanoids) yield break;

            if (CentralLoadManager.Instance == null) yield break;

            IManagedLoadable groupLoadable = new MapPortalAdapter(portal);

            CentralLoadManager.Instance.RegisterOrUpdateTask(groupLoadable);

            bool hasWorkToDo = CentralLoadManager.Instance.HasWork(groupLoadable, pawn) || WorkGiver_Utility.PawnHasNeededPuahItems(pawn, groupLoadable);
            if (!hasWorkToDo)
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Manager reports no work to do, and pawn has no relevant items in inventory.");
                yield break;
            }

            var remainingTransferables = groupLoadable.GetTransferables();
            var availableToClaim = CentralLoadManager.Instance.GetAvailableToClaim(groupLoadable, pawn);
            bool anyHaulableWorkLeft = false;
            if (remainingTransferables != null)
            {
                foreach (var transferable in remainingTransferables)
                {
                    if (transferable.CountToTransfer > 0 && transferable.HasAnyThing && availableToClaim.ContainsKey(transferable.ThingDef))
                    {
                        if (transferable.AnyThing is Pawn p && !Global_Utility.NeedsToBeCarried(p))
                        {
                            continue;
                        }
                        anyHaulableWorkLeft = true;
                        break;
                    }
                }
            }
            if (WorkGiver_Utility.PawnHasNeededPuahItems(pawn, groupLoadable))
            {
                anyHaulableWorkLeft = true;
            }
            if (!anyHaulableWorkLeft)
            {
                yield break;
            }

            DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> YES: All checks passed for Portal. Generating FloatMenuOption(s).");
            string label = "BulkLoadForTransporters.PriorityLoadCommand".Translate(parent.LabelShortCap);
            _cachedOptions.Add(new FloatMenuOption(label, () =>
            {
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wait, 2), JobTag.Misc);
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    var puahComp = pawn.TryGetComp<CompHauledToInventory>();
                    if (puahComp != null && puahComp.GetHashSet().Any())
                    {
                        if (!WorkGiver_Utility.TryCreateDirectedUnloadJob(pawn, groupLoadable, out _))
                        {
                            foreach (var thing in puahComp.GetHashSet().ToList())
                            {
                                pawn.inventory.innerContainer.TryDrop(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _);
                            }
                        }
                    }

                    if (WorkGiver_Utility.TryGiveBulkJob(pawn, groupLoadable, out Job job) && job.def != JobDefOf.Wait)
                    {
                        pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    }
                    else
                    {
                        Messages.Message("BulkLoadForTransporters.NoHaulPlanFound".Translate(pawn.LabelShort), MessageTypeDefOf.RejectInput);
                    }
                });
            }));

            foreach (var option in _cachedOptions)
            {
                yield return option;
            }
        }
    }
}