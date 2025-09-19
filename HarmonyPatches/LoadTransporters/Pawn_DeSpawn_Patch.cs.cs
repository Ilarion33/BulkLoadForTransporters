// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadTransporters/Pawn_DeSpawn_Patch.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Utils;
using HarmonyLib;
using Verse;

namespace BulkLoadForTransporters.HarmonyPatches.LoadTransporters
{
    /// <summary>
    /// Hooks into the moment a pawn is despawned from the map (due to death, leaving, etc.).
    /// This is a critical cleanup step to prevent "ghost reservations" where a task is claimed by a pawn that no longer exists, 
    /// causing the task to become permanently stuck.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), "DeSpawn")]
    public static class Pawn_DeSpawn_Patch
    {
        /// <summary>
        /// A prefix that runs just before a pawn is despawned.
        /// </summary>
        /// <param name="__instance">The pawn being despawned.</param>
        public static void Prefix(Pawn __instance)
        {
            if (__instance.RaceProps.Humanlike || __instance.RaceProps.IsMechanoid)
            {
                DebugLogger.LogMessage(LogCategory.Manager, () => $"Pawn_DeSpawn_Patch triggered for {__instance.LabelShort}. Releasing claims...");
                CentralLoadManager.Instance?.ReleaseClaimsForPawn(__instance);
            }
        }
    }
}