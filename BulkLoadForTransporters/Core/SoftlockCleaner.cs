// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/SoftlockCleaner.cs
using BulkLoadForTransporters.Core.Utils;
using PickUpAndHaul;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace BulkLoadForTransporters.Core
{
    /// <summary>
    /// A world component that periodically cleans up "soft-locked" items from pawns'
    /// Pick Up And Haul inventories. This prevents items from getting stuck on pawns
    /// who are incapable of or not assigned to hauling.
    /// </summary>
    public class SoftlockCleaner : WorldComponent
    {
        private const int CheckInterval = 1800; // Hardcoded as per design spec
        private Queue<Pawn> _pawnsToCheck;

        public SoftlockCleaner(World world) : base(world)
        {
            _pawnsToCheck = new Queue<Pawn>();
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();

            if (!LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableSoftlockCleaner)
            {
                if (_pawnsToCheck.Any()) _pawnsToCheck.Clear();
                return;
            }

            // Refill the queue every CheckInterval ticks
            if (Find.TickManager.TicksGame % CheckInterval == 0)
            {
                // Find all spawned pawns across all maps that belong to the player
                var allPlayerPawns = Find.Maps
                    .SelectMany(map => map.mapPawns.PawnsInFaction(Faction.OfPlayer));

                _pawnsToCheck = new Queue<Pawn>(allPlayerPawns);
            }

            // Process one pawn from the queue per tick (Time Slicing)
            if (_pawnsToCheck.Any())
            {
                var pawn = _pawnsToCheck.Dequeue();
                TryCleanPawn(pawn);
            }
        }

        private void TryCleanPawn(Pawn pawn)
        {
            // --- Stage 1: Fast preliminary checks ---
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Drafted) return;

            // Check if pawn has any PUAH items. If not, we are done.
            var puahComp = pawn.TryGetComp<CompHauledToInventory>();

            // 增加物理验证
            if (puahComp == null || !puahComp.GetHashSet().Any(thing => thing != null && !thing.Destroyed && thing.stackCount > 0)) return;

            // If the pawn is currently doing one of our jobs, they are fine.
            if (pawn.jobs?.curJob != null &&
                (JobDefRegistry.IsLoadingJob(pawn.jobs.curJob.def) || 
                JobDefRegistry.IsUnloadingJob(pawn.jobs.curJob.def) || 
                JobDefRegistry.IsCleanupJob(pawn.jobs.curJob.def)))
            {
                return;
            }

            // --- Stage 2: Check if the pawn is a "Softlock candidate" ---
            bool isSoftlocked = pawn.WorkTagIsDisabled(WorkTags.Hauling) ||
                                (pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.Hauling) == 0);

            if (!isSoftlocked) return;

            // --- Stage 3: Execute cleanup ---
            // At this point, we have a Softlocked pawn with PUAH items.
            // We drop ALL items registered in PUAH's inventory.
            var itemsToDrop = puahComp.GetHashSet().ToList(); // Create a copy to iterate over

            Log.Message($"[BulkLoad] SoftlockCleaner detected on {pawn.LabelShort}. Dropping {itemsToDrop.Count} soft-locked PUAH items.");

            foreach (var item in itemsToDrop)
            {
                if (item != null && !item.Destroyed && pawn.inventory.innerContainer.Contains(item))
                {
                    // Use the game's robust TryDrop method for each item.
                    if (!pawn.inventory.innerContainer.TryDrop(item, pawn.Position, pawn.Map, ThingPlaceMode.Near, out Thing _))
                    {
                        // As per design spec, if dropping fails, we abort and wait for the next cycle.
                        Log.Warning($"[BulkLoad] SoftlockCleaner failed to drop {item.LabelCap} from {pawn.LabelShort}. Will retry later.");
                        break;
                    }
                }
            }
        }
    }
}