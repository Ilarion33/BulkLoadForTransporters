// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadTransporters/Pawn_JobTracker_EndCurrentJob_Patch.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using BulkLoadForTransporters.Jobs;
using HarmonyLib;
using PickUpAndHaul;
using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.HarmonyPatches.LoadTransporters
{
    /// <summary>
    /// Hooks into the end of any pawn job. It's responsible for two critical safety features:
    /// 1. Releasing all claims from our CentralLoadManager when one of our jobs ends, for any reason.
    /// 2. If the job was interrupted while the pawn was carrying an item, it safely transfers
    ///     that item to the pawn's backpack and registers it with Pick Up And Haul.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), "EndCurrentJob")]
    public static class Pawn_JobTracker_EndCurrentJob_Patch
    {
        /// <summary>
        /// A prefix that runs before a job ends, giving us a final chance to clean up.
        /// </summary>
        public static void Prefix(Pawn_JobTracker __instance, JobCondition condition, Pawn ___pawn)
        {
            var jobToEnd = __instance.curJob;
            if (jobToEnd == null || !JobDefRegistry.IsLoadingJob(jobToEnd.def))
            {
                return;
            }

            // 检查是否有排队的 Job，并且这个 Job 是我们的“清理信使”
            var queuedJob = ___pawn.jobs.jobQueue.FirstOrDefault();
            if (queuedJob != null && queuedJob.job.loadID == JobDriver_Utility.CleanupJobLoadID)
            {
                DebugLogger.LogMessage(LogCategory.Manager, () => $"EndCurrentJob_Patch: Next job is a cleanup task ({queuedJob.job.def.defName}). RETAINING claims for {___pawn.LabelShort}.");
            }
            else if (queuedJob != null && queuedJob.job.def == jobToEnd.def)
            {
                DebugLogger.LogMessage(LogCategory.Manager, () => $"[EndCurrentJob Patch] RETAINING claims for {___pawn.LabelShort}. Reason: Chaining to a job of the same type ('{jobToEnd.def.defName}').");
            }
            else
            {
                DebugLogger.LogMessage(LogCategory.Manager, () => $"[EndCurrentJob Patch] RELEASING claims for {___pawn.LabelShort}. Job: {jobToEnd.def.defName}, Condition: {condition}. No retain condition met.");
                CentralLoadManager.Instance?.ReleaseClaimsForPawn(___pawn);
            }

            // 以下逻辑只在Job被“中断”时执行，这是为了处理非预期的状态。
            if (condition != JobCondition.Succeeded)
            {
                DebugLogger.LogMessage(LogCategory.Manager, () => "  - Job was INTERRUPTED. Checking for carried items to salvage...");
                if (!(__instance.curDriver is IBulkHaulState haulState))
                {
                    DebugLogger.LogMessage(LogCategory.Manager, () => "    - Current driver is not IBulkHaulState. No salvage possible.");
                    return;
                }


                var carriedThing = ___pawn.carryTracker.CarriedThing;
                // 确保Pawn手上有东西，并且这个东西确实是我们这次任务的一部分。
                if (carriedThing == null || !haulState.HauledThings.Contains(carriedThing))
                {
                    DebugLogger.LogMessage(LogCategory.Manager, () => $"    - Pawn is not carrying a task item. Carried: {carriedThing?.LabelCap ?? "null"}. Nothing to salvage.");
                    return;
                }

                var puahComp = ___pawn.TryGetComp<CompHauledToInventory>();
                if (puahComp == null)
                {
                    DebugLogger.LogMessage(LogCategory.Manager, () => "    - Pawn has no PUAH component. Cannot register salvaged item.");
                    return;
                }

                int countToStore = Global_Utility.CalculateSurplusToStoreInInventory(___pawn, carriedThing);

                DebugLogger.LogMessage(LogCategory.Manager, () => $"    - Salvaging {carriedThing.LabelCap}. Can store {countToStore}/{carriedThing.stackCount} in backpack (Cheat mode: {LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Core.Settings>().cheatIgnoreInventoryMass}).");

                if (countToStore > 0)
                {
                    Thing portionToStore = ___pawn.carryTracker.innerContainer.Take(carriedThing, countToStore);
                    if (portionToStore != null)
                    {
                        // HACK: canMerge必须为false！这可以防止任务物品与Pawn自己的“底货”错误合并，
                        // 从而保证PUAH注册的是一个独立的、干净的物品堆叠。
                        if (___pawn.inventory.innerContainer.TryAdd(portionToStore, false))
                        {
                            // 成功放入背包后，立即向PUAH注册，确保状态同步。
                            puahComp.RegisterHauledItem(portionToStore);
                            DebugLogger.LogMessage(LogCategory.Manager, () => $"      - SALVAGED {portionToStore.LabelCap} to backpack and registered with PUAH.");

                            if (haulState != null) 
                            {
                                haulState.RemoveHauledThing(carriedThing);
                            }
                        }
                        else
                        {
                            // 如果放入背包失败，则丢在地上，不注册
                            GenPlace.TryPlaceThing(portionToStore, ___pawn.Position, ___pawn.Map, ThingPlaceMode.Near);
                            DebugLogger.LogMessage(LogCategory.Manager, () => $"      - FAILED to add to backpack. Dropped {portionToStore.LabelCap} on ground.");
                        }
                    }
                }

                // 如果在放入部分物品后，手上还有剩余（因为负重不足），则将剩余部分也丢在地上。
                if (___pawn.carryTracker.CarriedThing != null)
                {
                    DebugLogger.LogMessage(LogCategory.Manager, () => $"    - Remaining portion of {___pawn.carryTracker.CarriedThing.LabelCap} dropped on ground.");
                    ___pawn.carryTracker.TryDropCarriedThing(___pawn.Position, ThingPlaceMode.Near, out Thing _);
                }
            }
        }
    }
}