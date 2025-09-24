// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadPortal/FloatMenuOption_LoadPortal_Patch.cs
using HarmonyLib;
using RimWorld;
using Verse;

namespace BulkLoadForTransporters.HarmonyPatches.LoadPortal
{    
    /// <summary>
    /// Prevents the vanilla "Prioritize hauling to portal" right-click menu option from appearing.
    /// </summary>
    [HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers), "GetWorkGiverOption")]
    public static class FloatMenuOption_LoadPortal_Patch
    {
        public static bool Prepare()
        {
            return LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>().enableBulkLoadPortal;
        }

        /// <summary>
        /// Before the game generates a menu option for any WorkGiver, we check if it's the one we want to suppress.
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(WorkGiverDef workGiver)
        {
            if (workGiver.giverClass == typeof(WorkGiver_HaulToPortal))
            {
                return false; 
            }
            return true;
        }
    }
}