// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadTransporters/AutoOpenTabs_Patch.cs
using HarmonyLib;
using RimWorld;
using Verse;
using System;

namespace BulkLoadForTransporters.HarmonyPatches.LoadTransporters
{
    /// <summary>
    /// A simple static class that acts as a "courier" to pass information between the detector patch and the executor patch.
    /// </summary>
    public static class AutoOpenTab_State
    {
        // NOTE: 这个静态字段在两个补丁之间传递状态
        public static Type nextTabToOpen = null;
    }

    /// <summary>
    /// The "Detector" patch. It runs after a pawn is selected and determines which tab, if any, should be automatically opened.
    /// </summary>
    [HarmonyPatch(typeof(Selector), nameof(Selector.Select))]
    public static class AutoOpenTabs_Selector_Patch
    {
        /// <summary>
        /// After a thing is selected, check if it's a type we care about and if the corresponding setting is enabled.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(Selector __instance)
        {
            // 每次选择都重置状态
            AutoOpenTab_State.nextTabToOpen = null;

            var settings = LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>();
            if (__instance.NumSelected != 1) return;

            Thing selectedThing = __instance.SingleSelectedThing;
            if (selectedThing == null) return;

            // --- 运输仓逻辑 ---
            if (settings.autoOpenTransporterContents && selectedThing.TryGetComp<CompTransporter>() != null)
            {
                AutoOpenTab_State.nextTabToOpen = typeof(ITab_ContentsTransporter);
                return;
            }

            // --- 驮兽逻辑 ---
            if (settings.autoOpenCarrierGear)
            {
                Pawn pawn = selectedThing as Pawn;
                if (pawn != null && pawn.GetTraderCaravanRole() == TraderCaravanRole.Carrier)
                {
                    AutoOpenTab_State.nextTabToOpen = typeof(ITab_Pawn_Gear);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// The "Executor" patch. It runs when the inspector pane's contents are drawn and executes the tab-opening request made by the detector.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Inspect), nameof(MainTabWindow_Inspect.DoWindowContents))]
    public static class AutoOpenTabs_InspectPane_Patch
    {
        /// <summary>
        /// After the inspector pane draws, check if there's a pending request to open a tab.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(MainTabWindow_Inspect __instance)
        {
            if (AutoOpenTab_State.nextTabToOpen == null) return;

            if (__instance.IsOpen && __instance.OpenTabType != AutoOpenTab_State.nextTabToOpen)
            {
                __instance.OpenTabType = AutoOpenTab_State.nextTabToOpen;
            }

            AutoOpenTab_State.nextTabToOpen = null;
        }
    }
}