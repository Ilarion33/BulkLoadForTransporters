// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/UnloadCarriers/FloatMenuOption_UnloadCarriers_Patch.cs
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.HarmonyPatches.UnloadCarriers
{
    /// <summary>
    /// Intercepts the right-click menu option for `WorkGiver_UnloadCarriers`
    /// to provide a custom, more descriptive label.
    /// </summary>
    [HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers), "GetWorkGiverOption")]
    public static class FloatMenuOption_UnloadCarriers_Patch
    {
        /// <summary>
        /// A Harmony Prepare method that conditionally applies this patch.
        /// It will only be applied if the "Bulk Unload Carriers" feature is enabled in settings.
        /// </summary>
        public static bool Prepare()
        {
            return LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>().enableBulkUnloadCarriers;
        }

        /// <summary>
        /// After the original `GetWorkGiverOption` creates a menu option, this postfix
        /// checks if it's for `WorkGiver_UnloadCarriers` and, if so, replaces its label.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(ref FloatMenuOption __result, Pawn pawn, WorkGiverDef workGiver, LocalTargetInfo target)
        {
            if (workGiver.giverClass == typeof(WorkGiver_UnloadCarriers) && __result != null && !__result.Disabled)
            {
                // 获取目标Pawn的标签
                Pawn targetPawn = target.Pawn;
                if (targetPawn == null) return;

                __result.Label = "BulkLoadForTransporters.PriorityUnloadCommand".Translate(targetPawn.LabelShortCap);
            }
        }
    }
}