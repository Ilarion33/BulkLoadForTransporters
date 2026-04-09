// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/DeliverConstruction/DeconstructForBlueprint_Disabler_Patch.cs
using BulkLoadForTransporters.Core;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BulkLoadForTransporters.HarmonyPatches.DeliverConstruction
{
    /// <summary>
    /// Implements the "Silent Guard" strategy.
    /// This patch effectively disables the specialized WorkGiver for "deconstruct for blueprint" tasks
    /// when our bulk construction delivery feature is active. By making this WorkGiver always report
    /// "no job", we force the game's AI to fall back to the more general WorkGiver_ConstructDeliverResources,
    /// which we have already fully patched to initiate our "Optimistic Haul" logic.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DeconstructForBlueprint), "HasJobOnThing")]
    public static class DeconstructForBlueprint_Disabler_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            // If our feature is enabled, this WorkGiver should be silent.
            if (LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkConstructionDelivery)
            {
                __result = false;
                return false; // Report "no job" and skip the original method entirely.
            }

            // If our feature is disabled, let the original game logic run as intended.
            return true;
        }
    }
}