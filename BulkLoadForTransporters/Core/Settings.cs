// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Settings.cs
using Verse;

namespace BulkLoadForTransporters.Core
{
    /// <summary>
    /// Handles the storage and serialization of all mod settings.
    /// This class is loaded by RimWorld's Mod base class.
    /// </summary>
    public class Settings : ModSettings
    {
        // --- Feature Master Switches ---
        public bool enableBulkLoadTransporters = true;
        public bool enableBulkLoadPortal = true;
        public bool enableBulkLoadVehicles = true;
        public bool enableBulkUnloadCarriers = true;

        // --- General Settings ---
        public int AiUpdateFrequency = 60;
        public int visualUnloadDelay = 15;
        public bool cleanupOnSave = true;

        // --- Bulk Loading Settings (Shared by Transporters and Portals) ---
        public float opportunityScanRadius = 40f;
        //public float stopPlanningAtPercent = 0.98f;
        public int pathfindingHeuristicCandidates = 3;
        public bool enableContinuousLoading = true;
        public bool autoOpenTransporterContents = true;


        // --- Bulk Unloading Settings ---
        public float minFreeSpaceToUnloadCarrierPct = 0.5f;
        public bool reserveCarrierOnUnload = false;
        public bool autoOpenCarrierGear = true;


        public bool logPlanner = false;
        public bool logWorkGiver = false;
        public bool logToils = false;
        public bool logManager = false;


        /// <summary>
        /// Handles saving and loading the settings to/from the mod's configuration file.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();

            // --- Master Switches ---
            Scribe_Values.Look(ref enableBulkLoadTransporters, "enableBulkLoadTransporters", true);
            Scribe_Values.Look(ref enableBulkLoadPortal, "enableBulkLoadPortal", true);
            Scribe_Values.Look(ref enableBulkLoadVehicles, "enableBulkLoadVehicles", true);
            Scribe_Values.Look(ref enableBulkUnloadCarriers, "enableBulkUnloadCarriers", true);


            // --- General ---
            Scribe_Values.Look(ref AiUpdateFrequency, "AiUpdateFrequency", 60);
            Scribe_Values.Look(ref visualUnloadDelay, "visualUnloadDelay", 15);
            Scribe_Values.Look(ref cleanupOnSave, "cleanupOnSave", true);

            Scribe_Values.Look(ref logPlanner, "logPlanner", false);
            Scribe_Values.Look(ref logWorkGiver, "logWorkGiver", false);
            Scribe_Values.Look(ref logToils, "logToils", false);
            Scribe_Values.Look(ref logManager, "logManager", false);


            // --- Bulk Loading ---
            Scribe_Values.Look(ref opportunityScanRadius, "opportunityScanRadius", 40f);
            //Scribe_Values.Look(ref stopPlanningAtPercent, "stopPlanningAtPercent", 0.98f);
            Scribe_Values.Look(ref pathfindingHeuristicCandidates, "pathfindingHeuristicCandidates", 3);
            Scribe_Values.Look(ref enableContinuousLoading, "enableContinuousLoading", true);
            Scribe_Values.Look(ref autoOpenTransporterContents, "autoOpenTransporterContents", true);


            // --- Bulk Unloading ---
            Scribe_Values.Look(ref minFreeSpaceToUnloadCarrierPct, "minFreeSpaceToUnloadCarrierPct", 0.5f);
            Scribe_Values.Look(ref reserveCarrierOnUnload, "reserveCarrierOnUnload", false);
            Scribe_Values.Look(ref autoOpenCarrierGear, "autoOpenCarrierGear", true);

        }
    }
}