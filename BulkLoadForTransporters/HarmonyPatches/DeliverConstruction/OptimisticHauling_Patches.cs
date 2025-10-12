// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/DeliverConstruction/OptimisticHauling_Patches.cs
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.HarmonyPatches.DeliverConstruction
{
    /// <summary>
    /// A collection of patches that implement the "deception" part of the "Optimistic Haul" strategy.
    /// These patches can be temporarily activated via a global flag to make the game's planning logic
    /// ignore physical blockers and pre-construction tasks like removing floors.
    /// </summary>
    public static class OptimisticHaulingController
    {
        // The single, global flag that controls all deception patches.
        public static bool IsInBulkPlanningPhase = false;
    }

    // --- Patch 1: Deceive about physical blockers ---
    [HarmonyPatch(typeof(GenConstruct), "FirstBlockingThing")]
    public static class GenConstruct_FirstBlockingThing_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref Thing __result)
        {
            if (OptimisticHaulingController.IsInBulkPlanningPhase)
            {
                __result = null;
                return false; // Deception active: report no blockers.
            }
            return true; // Deception off: run original check.
        }
    }

    // --- Patch 2: Deceive about the need to remove floors ---
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "ShouldRemoveExistingFloorFirst")]
    public static class WorkGiver_ConstructDeliverResources_ShouldRemoveExistingFloorFirst_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            if (OptimisticHaulingController.IsInBulkPlanningPhase)
            {
                __result = false;
                return false; // Deception active: report that floors never need to be removed first.
            }
            return true; // Deception off: run original check.
        }
    }

    // --- Patch 3: Deceive about creating a floor-removal job (Double insurance) ---
    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "RemoveExistingFloorJob")]
    public static class WorkGiver_ConstructDeliverResources_RemoveExistingFloorJob_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref Job __result)
        {
            if (OptimisticHaulingController.IsInBulkPlanningPhase)
            {
                __result = null;
                return false; // Deception active: never create a floor removal job.
            }
            return true; // Deception off: run original check.
        }
    }
}