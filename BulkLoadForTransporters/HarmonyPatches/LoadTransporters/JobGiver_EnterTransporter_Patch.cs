// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadTransporters/JobGiver_EnterTransporter_Patch.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using HarmonyLib;
using RimWorld;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace BulkLoadForTransporters.HarmonyPatches.LoadTransporters
{
    /// <summary>
    /// Intercepts the AI logic for pawns deciding to enter a transporter (like a shuttle).
    /// It acts as a gatekeeper, preventing pawns from boarding if there is still hauling work to be done for that specific transport group.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_EnterTransporter), "TryGiveJob")]
    public static class JobGiver_EnterTransporter_Patch
    {
        /// <summary>
        /// A conditional method for Harmony. The patch will only be applied if the bulk loading feature is enabled in the mod settings.
        /// </summary>
        public static bool Prepare()
        {
            return LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>().enableBulkLoadTransporters;
        }

        /// <summary>
        /// A prefix that overrides the decision to enter a transporter.
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, ref Job __result)
        {
            // NOTE: 以下均为原版逻辑检查，用于快速过滤掉不相关的场景，确保我们只在“装载运输仓”任务中介入。
            Lord lord = pawn.GetLord();
            if (lord == null || !(lord.LordJob is LordJob_LoadAndEnterTransporters)) return true;

            var duty = pawn.mindState.duty;
            if (duty == null || duty.def != DutyDefOf.LoadAndEnterTransporters) return true;

            int transportersGroup = duty.transportersGroup;
            if (transportersGroup < 0) return true;

            var anyTransporterInGroup = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Transporter)
                .Select(t => t.TryGetComp<CompTransporter>())
                .FirstOrDefault(tr => tr != null && tr.groupID == transportersGroup);

            if (anyTransporterInGroup == null) return true;

            // 创建一个 Adapter 以便与 Manager 交互
            IManagedLoadable loadable = new LoadTransportersAdapter(anyTransporterInGroup);

            // 条件1: 对于 pawn 自己来说，是否还有能做的活？
            bool pawnCanWork = WorkGiver_Utility.HasPotentialBulkWork(pawn, loadable);

            // 条件2: 对于整个任务来说，是否还有其他人在途？
            bool anyClaimsInProgress = CentralLoadManager.Instance.AnyClaimsInProgress(loadable);

            // 只有当“pawn自己没活干” 并且 “也没有任何其他在途的货物”时，才允许登船
            if (pawnCanWork || anyClaimsInProgress)
            {
                // 还有物品相关的活要干，阻止登船
                __result = null;
                return false;
            }

            // 所有物品都已搞定，放行，让原版逻辑处理登船
            return true;
        }
    }
}