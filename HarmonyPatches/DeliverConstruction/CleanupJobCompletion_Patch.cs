// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/DeliverConstruction/CleanupJobCompletion_Patch.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Utils;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.HarmonyPatches.DeliverConstruction
{
    /// <summary>
    /// Implements the "Dual Clipboard" safe handover strategy.
    /// This patch ensures that claims are ALWAYS released when a cleanup job ends,
    /// while the relay action is ONLY triggered on success.
    /// Prefix identifies the messenger and records its end state.
    /// Postfix reads the record, performs the necessary actions, and clears the record.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), "EndCurrentJob")]
    public static class Pawn_JobTracker_EndCurrentJob_CleanupRelay_Patch
    {
        private static readonly HashSet<JobDef> CleanupJobDefs = new HashSet<JobDef>
        {
            JobDefOf.CutPlant,
            JobDefOf.Deconstruct,
            JobDefOf.RemoveFloor,
            JobDefOf.HaulToCell
        };

        private static readonly AccessTools.FieldRef<Pawn_JobTracker, Pawn> PawnRef =
            AccessTools.FieldRefAccess<Pawn_JobTracker, Pawn>("pawn");

        // --- “双重剪贴板” ---
        // Item1: The pawn who performed the job.
        // Item2: The condition the job ended with.
        [ThreadStatic]
        private static Tuple<Pawn, JobCondition> _messengerInfo;

        [HarmonyPrefix]
        public static void Prefix(Pawn_JobTracker __instance, JobCondition condition, bool startNewJob)
        {
            // 识别信使，并将其信息记录到剪贴板
            Job jobToEnd = __instance.curJob;

            if (jobToEnd == null ||
                jobToEnd.loadID != JobDriver_Utility.CleanupJobLoadID ||
                !JobDefRegistry.IsCleanupJob(jobToEnd.def))
            {
                return;
            }

            Pawn pawn = PawnRef(__instance);
            if (pawn == null) return;

            // 记录信使的身份和任务结果
            _messengerInfo = new Tuple<Pawn, JobCondition>(pawn, condition);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            // --- 读取剪贴板，执行操作，然后销毁 ---
            if (_messengerInfo != null)
            {
                // 读取并销毁记录，确保一次性
                var (pawn, condition) = _messengerInfo;
                _messengerInfo = null;

                // 无条件释放认领 (双保险的核心)
                //DebugLogger.LogMessage(LogCategory.Toils, () => $"[CleanupRelay | Postfix] Messenger job for {pawn.LabelShort} ended with '{condition}'. Releasing claims NOW.");
                //CentralLoadManager.Instance.ReleaseClaimsForPawn(pawn);

                // 只有在“成功”时，才尝试“接力”
                if (condition == JobCondition.Succeeded)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"[CleanupRelay | Postfix]   -> Condition was Succeeded. Queuing relay action.");

                    WorkGiver_Utility.TryChainToNextConstructionJob(pawn);
                }
                else
                {
                    CentralLoadManager.Instance.ReleaseClaimsForPawn(pawn);
                }
            }
        }
    }
}