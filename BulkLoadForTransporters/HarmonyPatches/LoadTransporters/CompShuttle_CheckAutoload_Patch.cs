// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadTransporters/CompShuttle_CheckAutoload_Patch.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BulkLoadForTransporters.HarmonyPatches.LoadTransporters
{
    /// <summary>
    /// Patches `CompShuttle.CheckAutoload` to prevent it from creating conflicting hauling jobs while a bulk load operation is managed by our system.
    /// </summary>
    [HarmonyPatch(typeof(CompShuttle), "CheckAutoload")]
    public static class CompShuttle_CheckAutoload_Patch
    {
        public static bool Prepare()
        {
            return LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkLoadTransporters;
        }

        /// <summary>
        /// Before `CheckAutoload` runs, we check with our CentralLoadManager.
        /// If our system has active claims on this shuttle, we temporarily set `autoload` to false, effectively disabling the vanilla logic.
        /// We store the original state of `autoload` in `__state` to be restored later.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix(CompShuttle __instance, out bool __state)
        {
            __state = __instance.Autoload;

            if (CentralLoadManager.Instance == null) return;

            IManagedLoadable loadable = new LoadTransportersAdapter(__instance.Transporter);

            // 直接查询 Manager，看我们的系统是否正在处理这个任务
            if (CentralLoadManager.Instance.AnyClaimsInProgress(loadable))
            {
                // HACK: 使用 Traverse 来修改私有字段。这是在不改变原方法签名的前提下，
                // 暂时禁用其功能的标准做法。
                Traverse.Create(__instance).Field("autoload").SetValue(false);
            }
        }

        /// <summary>
        /// After `CheckAutoload` (and any other patches) have finished,
        /// this Postfix runs to unconditionally restore the original `autoload` state.
        /// This ensures the shuttle's behavior returns to normal after our check.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix(CompShuttle __instance, bool __state)
        {
            // 从 __state 中恢复 autoload 的原始值
            Traverse.Create(__instance).Field("autoload").SetValue(__state);
        }
    }
}