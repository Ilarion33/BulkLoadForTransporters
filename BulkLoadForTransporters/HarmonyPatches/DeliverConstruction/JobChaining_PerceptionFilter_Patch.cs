// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/DeliverConstruction/JobChaining_PerceptionFilter_Patch.cs
using HarmonyLib;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.HarmonyPatches.DeliverConstruction
{
    public static class PerceptionFilterController
    {
        public static bool IsActive = false;
        public static IntVec3 Center;
        public static float RadiusSquared;
    }

    /// <summary>
    /// The final, correct implementation of the "Perception Filter".
    /// It intercepts calls to the core reachability checking method within the Reachability class instance.
    /// This ensures all pathfinding-based reachability checks are filtered.
    /// </summary>
    [HarmonyPatch(typeof(Reachability), "CanReach",
        new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) })]
    public static class Reachability_CanReach_PerceptionFilterPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result, LocalTargetInfo dest)
        {
            if (PerceptionFilterController.IsActive)
            {
                if (dest.Cell.DistanceToSquared(PerceptionFilterController.Center) > PerceptionFilterController.RadiusSquared)
                {
                    __result = false;
                    return false; // Skip original method
                }
            }
            return true; // Let original method run
        }
    }
}