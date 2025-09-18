// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadTransporters/Game_DeinitAndRemoveMap_Patch.cs
using BulkLoadForTransporters.Core;
using HarmonyLib;
using Verse;

namespace BulkLoadForTransporters.HarmonyPatches.LoadTransporters
{
    /// <summary>
    /// Hooks into the moment a map is about to be destroyed and removed from the game.
    /// Its sole responsibility is to notify the CentralLoadManager to clean up any data associated with the outgoing map, 
    /// preventing dangling references and memory leaks.
    /// </summary>
    [HarmonyPatch(typeof(Game), "DeinitAndRemoveMap")]
    public static class Game_DeinitAndRemoveMap_Patch
    {
        /// <summary>
        /// A prefix that runs just before a map is de-initialized.
        /// </summary>
        /// <param name="map">The map that is about to be removed.</param>
        public static void Prefix(Map map)
        {
            CentralLoadManager.Instance?.Notify_MapRemoved(map);
        }
    }
}