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
        public bool enableBulkConstructionDelivery = true;

        // --- General Settings ---
        public int AiUpdateFrequency = 60;
        public int visualUnloadDelay = 15;
        public bool cleanupOnSave = true;
        public bool enableSoftlockCleaner = true;
        public bool cheatIgnoreInventoryMass = true;

        // --- Construction Delivery ---
        public int constructionGroupingGridSize = 32;
        public float constructionChainScanRadius = 20f;

        // --- Bulk Loading Settings ---
        public float opportunityScanRadius = 90f;
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
            Scribe_Values.Look(ref enableBulkConstructionDelivery, "enableBulkConstructionDelivery", true);


            // --- General ---
            Scribe_Values.Look(ref AiUpdateFrequency, "AiUpdateFrequency", 60);
            Scribe_Values.Look(ref visualUnloadDelay, "visualUnloadDelay", 15);
            Scribe_Values.Look(ref cleanupOnSave, "cleanupOnSave", true);
            Scribe_Values.Look(ref enableSoftlockCleaner, "enableSoftlockCleaner", true);
            Scribe_Values.Look(ref cheatIgnoreInventoryMass, "cheatIgnoreInventoryMass", true);

            Scribe_Values.Look(ref logPlanner, "logPlanner", false);
            Scribe_Values.Look(ref logWorkGiver, "logWorkGiver", false);
            Scribe_Values.Look(ref logToils, "logToils", false);
            Scribe_Values.Look(ref logManager, "logManager", false);

            Scribe_Values.Look(ref constructionGroupingGridSize, "constructionGroupingGridSize", 32);
            Scribe_Values.Look(ref constructionChainScanRadius, "constructionChainScanRadius", 20f);

            // --- Bulk Loading ---
            Scribe_Values.Look(ref opportunityScanRadius, "opportunityScanRadius", 90f);
            //Scribe_Values.Look(ref stopPlanningAtPercent, "stopPlanningAtPercent", 0.98f);
            Scribe_Values.Look(ref pathfindingHeuristicCandidates, "pathfindingHeuristicCandidates", 3);
            Scribe_Values.Look(ref enableContinuousLoading, "enableContinuousLoading", true);
            Scribe_Values.Look(ref autoOpenTransporterContents, "autoOpenTransporterContents", true);


            // --- Bulk Unloading ---
            Scribe_Values.Look(ref minFreeSpaceToUnloadCarrierPct, "minFreeSpaceToUnloadCarrierPct", 0.5f);
            Scribe_Values.Look(ref reserveCarrierOnUnload, "reserveCarrierOnUnload", false);
            Scribe_Values.Look(ref autoOpenCarrierGear, "autoOpenCarrierGear", true);

        }

        public void Reset()
        {
            var defaultSettings = new Settings();

            // --- Feature Master Switches ---
            this.enableBulkLoadTransporters = defaultSettings.enableBulkLoadTransporters;
            this.enableBulkLoadPortal = defaultSettings.enableBulkLoadPortal;
            this.enableBulkLoadVehicles = defaultSettings.enableBulkLoadVehicles;
            this.enableBulkUnloadCarriers = defaultSettings.enableBulkUnloadCarriers;
            this.enableBulkConstructionDelivery = defaultSettings.enableBulkConstructionDelivery;

            // --- General Settings ---
            this.AiUpdateFrequency = defaultSettings.AiUpdateFrequency;
            this.visualUnloadDelay = defaultSettings.visualUnloadDelay;
            this.cleanupOnSave = defaultSettings.cleanupOnSave;
            this.enableSoftlockCleaner = defaultSettings.enableSoftlockCleaner;
            this.cheatIgnoreInventoryMass = defaultSettings.cheatIgnoreInventoryMass;

            // --- Construction Delivery ---
            this.constructionGroupingGridSize = defaultSettings.constructionGroupingGridSize;
            this.constructionChainScanRadius = defaultSettings.constructionChainScanRadius;

            // --- Bulk Loading Settings ---
            this.opportunityScanRadius = defaultSettings.opportunityScanRadius;
            this.pathfindingHeuristicCandidates = defaultSettings.pathfindingHeuristicCandidates;
            this.enableContinuousLoading = defaultSettings.enableContinuousLoading;
            this.autoOpenTransporterContents = defaultSettings.autoOpenTransporterContents;

            // --- Bulk Unloading Settings ---
            this.minFreeSpaceToUnloadCarrierPct = defaultSettings.minFreeSpaceToUnloadCarrierPct;
            this.reserveCarrierOnUnload = defaultSettings.reserveCarrierOnUnload;
            this.autoOpenCarrierGear = defaultSettings.autoOpenCarrierGear;

            // --- Debug Logging Settings ---
            this.logPlanner = defaultSettings.logPlanner;
            this.logWorkGiver = defaultSettings.logWorkGiver;
            this.logToils = defaultSettings.logToils;
            this.logManager = defaultSettings.logManager;
        }
    }
}