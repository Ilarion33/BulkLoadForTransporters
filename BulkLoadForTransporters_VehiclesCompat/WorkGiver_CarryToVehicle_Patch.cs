// Copyright (c) 2025 Ilarion. All rights reserved.
//
// BulkLoadForTransporters_VehiclesCompat/WorkGiver_CarryToVehicle_Patch.cs
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Vehicles;
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters_VehiclesCompat;

namespace BulkLoadForTransporters_VehiclesCompat
{
    /// <summary>
    /// Patches the HasJobOnThing method in the WorkGiver_Scanner base class.
    /// This is a performance optimization. We add a type check to ensure it only affects
    /// WorkGivers that are for carrying to vehicles.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_Scanner), "HasJobOnThing")]
    public static class WorkGiver_Scanner_HasJobOnThing_Patch_ForVehicles
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result, WorkGiver_Scanner __instance, Pawn pawn, Thing t, bool forced)
        {
            if (!LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkLoadVehicles)
            {
                return true;
            }

            // This patch applies to ALL WorkGiver_Scanners. We must filter to only affect vehicle loading.
            if (__instance is WorkGiver_CarryToVehicle)
            {
                if (t is VehiclePawn vehicle)
                {
                    IManagedLoadable groupLoadable = new VehicleAdapter(vehicle);
                    __result = LoadTransporters_WorkGiverUtility.HasPotentialBulkWork(pawn, groupLoadable);
                    return false; // Prevent original method
                }
            }

            // For all other WorkGiver_Scanners, or if the target isn't a vehicle, allow the original logic to run.
            return true;
        }
    }

    /// <summary>
    /// Patches the JobOnThing method directly in the abstract base class for carrying to vehicles.
    /// This is the primary entry point for replacing the job creation logic.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_CarryToVehicle), "JobOnThing")]
    public static class WorkGiver_CarryToVehicle_JobOnThing_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref Job __result, WorkGiver_CarryToVehicle __instance, Pawn pawn, Thing t, bool forced)
        {
            if (!LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkLoadVehicles)
            {
                return true;
            }

            if (t is VehiclePawn vehicle)
            {
                IManagedLoadable groupLoadable = new VehicleAdapter(vehicle);
                LoadTransporters_WorkGiverUtility.TryGiveBulkJob(pawn, groupLoadable, out __result);
                return false; // Prevent original method
            }
            return true; // Allow original method for non-vehicle things
        }
    }    
}