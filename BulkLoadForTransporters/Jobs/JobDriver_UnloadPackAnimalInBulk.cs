// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/JobDriver_UnloadPackAnimalInBulk.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Toils_UnloadCarriers;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Jobs
{
    /// <summary>
    /// The primary JobDriver for the "Bulk Unload Carriers" feature.
    /// It orchestrates a sequence of Toils to efficiently transfer items from a
    /// carrier pawn (like a pack animal) to the hauler's own inventory.
    /// </summary>
    public class JobDriver_UnloadPackAnimalInBulk : JobDriver_BulkLoadBase
    {
        /// <summary>
        /// Provides the text report that appears in the pawn's inspection pane.
        /// </summary>
        public override string GetReport()
        {
            Thing carrier = job.GetTarget(TargetIndex.A).Thing;
            if (carrier != null)
            {
                return "BulkLoadForTransporters.ReportString.Unloading".Translate(carrier.LabelShortCap);
            }
            return "BulkLoadForTransporters.ReportString.Unloading_Generic".Translate();
        }

        /// <summary>
        /// Handles the reservation of the target carrier.
        /// The behavior is configurable in the mod settings.
        /// </summary>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // 根据设置决定是否执行预定
            if (LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>().reserveCarrierOnUnload)
            {
                // “断言”模式：进行标准的、排他性的预定
                return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed);
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// Constructs the sequence of Toils that defines the Job's behavior.
        /// </summary>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            if (pawn.Map == null)
            {
                this.ResetToilIndex();
                yield break;
            }

            // 注册中断安全的回滚逻辑。
            // NOTE: 这个FinishAction只在任务被中断或失败时触发。
            // 任务成功完成的路径由最后一个Toil(Toil_FinalizeUnload)专门处理。
            this.AddFinishAction(jobCondition =>
            {
                if (jobCondition != JobCondition.Succeeded)
                {
                    this.ReconcileStateWithPuah(jobCondition);
                }
            });

            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOnForbidden(TargetIndex.A);

            // 注册一个周期性的检查，以应对目标驮兽被其他人预定的情况。
            this.FailOn(() =>
            {
                if (pawn.IsHashIntervalTick(LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Settings>().AiUpdateFrequency))
                {
                    var carrier = this.job.GetTarget(TargetIndex.A).Thing;
                    if (carrier == null) return true; // 如果目标没了，就失败

                    // 我们需要手动遍历预定列表，来检查是否有“除自己以外”的任何人预定了目标。
                    // NOTE: 这是因为`pawn.CanReserve(carrier)`会因为自己已经预定了而返回false，无法用于此场景。
                    var reservations = this.pawn.Map.reservationManager.ReservationsReadOnly;
                    foreach (var res in reservations)
                    {
                        if (res.Target == carrier && res.Claimant != this.pawn)
                        {
                            // 找到了一个其他人的预定，任务失败。
                            return true;
                        }
                    }
                }
                return false;
            });

            // 走向目标驮兽。
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil unloadLoopStart = ToilMaker.MakeToil("UnloadLoopStart");
            Toil unloadLoopEnd = ToilMaker.MakeToil("UnloadLoopEnd");

            yield return unloadLoopStart;
            // 决策：从驮兽背包中选择下一个要拿的物品。
            yield return Toil_SelectNextItemFromContainer.Create(TargetIndex.A, this, unloadLoopEnd);
            // 模拟卸货动作的视觉延迟。
            yield return Toils_General.Wait(LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Settings>().visualUnloadDelay, TargetIndex.A);
            // 执行：将决策好的物品从驮兽背包转移到自己身上。
            yield return Toil_TransferFromContainer.Create(TargetIndex.A, TargetIndex.C, this);
            // 回到循环开始，进行下一次决策。
            yield return Toils_Jump.Jump(unloadLoopStart);

            yield return unloadLoopEnd;

            // 任务成功完成的收尾工作。
            // NOTE: 这个Toil负责处理手持物品的衔接和背包物品的PUAH注册。
            yield return Toil_FinalizeUnload.Create();
        }
    }
}