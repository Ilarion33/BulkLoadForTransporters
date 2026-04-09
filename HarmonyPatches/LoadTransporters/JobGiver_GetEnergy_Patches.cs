// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadTransporters/JobGiver_GetEnergy_Charger_Patch.cs
using BulkLoadForTransporters.Core;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.HarmonyPatches.LoadTransporters
{
    /// <summary>
    /// When a mechanical unit is assigned a charging task, it actively releases all its loaded task claims.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_GetEnergy_Charger), "TryGiveJob")]
    public static class JobGiver_GetEnergy_Charger_Patch
    {
        public static void Postfix(Pawn pawn, Job __result)
        {
            if (__result != null && pawn != null && (pawn.RaceProps.IsMechanoid || pawn.RaceProps.Humanlike))
            {
                CentralLoadManager.Instance?.ReleaseClaimsForPawn(pawn);
            }
        }
    }

    /// <summary>
    /// When a machine is assigned a task to revive from hibernation, it actively releases all its loaded task claims.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_GetEnergy_SelfShutdown), "TryGiveJob")]
    public static class JobGiver_GetEnergy_SelfShutdown_Patch
    {
        public static void Postfix(Pawn pawn, Job __result)
        {
            if (__result != null && pawn != null && (pawn.RaceProps.IsMechanoid || pawn.RaceProps.Humanlike))
            {
                CentralLoadManager.Instance?.ReleaseClaimsForPawn(pawn);
            }
        }
    }
}