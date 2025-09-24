// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadTransporters/ScribeSaver_InitSaving_Patch.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Utils;
using HarmonyLib;
using Verse;

namespace BulkLoadForTransporters.HarmonyPatches.LoadTransporters
{
    /// <summary>
    /// Hooks into the game's saving process to perform pre-save cleanup.
    /// </summary>
    [HarmonyPatch(typeof(ScribeSaver), "InitSaving")]
    public static class ScribeSaver_InitSaving_Patch
    {
        /// <summary>
        /// A prefix that runs before the game starts writing to a save file.
        /// </summary>
        public static void Prefix()
        {
            // NOTE: 这是一个重要的安全功能，用于防止因未完成的Job而导致的存档损坏。
            // 只有在用户于设置中启用了此选项时才运行。
            if (LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().cleanupOnSave)
            {
                DebugLogger.LogMessage(LogCategory.Manager, () => "ScribeSaver_InitSaving_Patch triggered. Running pre-save cleanup...");
                SafeUnloadManager.CleanupBeforeSaving();
            }
        }
    }
}