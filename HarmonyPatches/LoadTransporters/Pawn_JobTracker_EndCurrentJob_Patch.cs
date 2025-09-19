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
            DebugLogger.LogMessage(LogCategory.Manager, () => $"EndCurrentJob_Patch triggered for {___pawn.LabelShort}. Job: {jobToEnd.def.defName}, Condition: {condition}.");

            // 无论Job因何结束（成功、失败、中断），都必须释放认领，确保任务状态的最终一致性。
            CentralLoadManager.Instance?.ReleaseClaimsForPawn(___pawn);

            // 以下逻辑只在Job被“中断”时执行，这是为了处理非预期的状态。
            if (condition == JobCondition.InterruptForced || condition == JobCondition.InterruptOptional)
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

                // 检查背包剩余负重，决定能将多少手持物品安全地放回背包。
                float availableMass = MassUtility.FreeSpace(___pawn);
                float massPerItem = carriedThing.GetStatValue(StatDefOf.Mass);
                int countToStore = (availableMass > 0 && massPerItem > 0) ? Mathf.FloorToInt(availableMass / massPerItem) : 0;
                countToStore = Mathf.Min(carriedThing.stackCount, countToStore);
                DebugLogger.LogMessage(LogCategory.Manager, () => $"    - Salvaging {carriedThing.LabelCap}. Can store {countToStore}/{carriedThing.stackCount} in backpack.");

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