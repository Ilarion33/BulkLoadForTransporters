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
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
        {
            // NOTE: 这是Portal功能的总开关。
            if (!LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkLoadPortal)
            {
                yield break;
            }

            // 所有前置检查
            if (pawn.Drafted) yield break;
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)) yield break;
            if (pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.Hauling) == 0) yield break;
            if (!(pawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Deadly))) yield break;

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

            bool hasWorkToDo = CentralLoadManager.Instance.HasWork(groupLoadable) || BulkLoad_Utility.PawnHasNeededPuahItems(pawn, groupLoadable);
            if (!hasWorkToDo) yield break;

            var remainingTransferables = groupLoadable.GetTransferables();
            var availableToClaim = CentralLoadManager.Instance.GetAvailableToClaim(groupLoadable);
            bool anyHaulableWorkLeft = false;
            if (remainingTransferables != null)
            {
                foreach (var transferable in remainingTransferables)
                {
                    if (transferable.CountToTransfer > 0 && transferable.HasAnyThing && availableToClaim.ContainsKey(transferable.ThingDef))
                    {
                        if (transferable.AnyThing is Pawn p && !LoadTransporters_WorkGiverUtility.NeedsToBeCarried(p))
                        {
                            continue;
                        }
                        anyHaulableWorkLeft = true;
                        break;
                    }
                }
            }
            if (BulkLoad_Utility.PawnHasNeededPuahItems(pawn, groupLoadable))
            {
                anyHaulableWorkLeft = true;
            }
            if (!anyHaulableWorkLeft)
            {
                yield break;
            }

            string label = "BulkLoadForTransporters.PriorityLoadCommand".Translate(parent.LabelShortCap);
            yield return new FloatMenuOption(label, () =>
            {
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wait, 2), JobTag.Misc);
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    var puahComp = pawn.TryGetComp<CompHauledToInventory>();
                    if (puahComp != null && puahComp.GetHashSet().Any())
                    {
                        if (!BulkLoad_Utility.TryCreateDirectedUnloadJob(pawn, groupLoadable, out _))
                        {
                            foreach (var thing in puahComp.GetHashSet().ToList())
                            {
                                pawn.inventory.innerContainer.TryDrop(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _);
                            }
                        }
                    }

                    if (LoadTransporters_WorkGiverUtility.TryGiveBulkJob(pawn, groupLoadable, out Job job) && job.def != JobDefOf.Wait)
                    {
                        pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    }
                    else
                    {
                        Messages.Message("BulkLoadForTransporters.NoHaulPlanFound".Translate(pawn.LabelShort), MessageTypeDefOf.RejectInput);
                    }
                });
            });

            
        }
    }
}