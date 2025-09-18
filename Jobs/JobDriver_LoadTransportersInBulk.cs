// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/JobDriver_LoadTransportersInBulk.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using BulkLoadForTransporters.Toils_LoadTransporters;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Jobs
{
    /// <summary>
    /// The primary JobDriver for the "Bulk Load Transporters" feature.
    /// It orchestrates a complex sequence of Toils to plan, pick up, and unload
    /// various items for a CompTransporter-based loading task.
    /// </summary>
    public class JobDriver_LoadTransportersInBulk : JobDriver_BulkLoadBase
    {
        /// <summary>
        /// Provides the text report that appears in the pawn's inspection pane.
        /// </summary>
        public override string GetReport()
        {
            if (job.targetQueueA.NullOrEmpty())
            {
                if (job.targetB.HasThing)
                {
                    return "BulkLoadForTransporters.ReportString.Loading".Translate(job.targetB.Thing.LabelShortCap);
                }
            }
            return "BulkLoadForTransporters.ReportString.Loading_Generic".Translate();
        }

        /// <summary>
        /// Constructs the sequence of Toils that defines the Job's behavior.
        /// This method is the "script" that the pawn follows.
        /// </summary>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            // NOTE: 这是保证任务在任何情况下（成功、失败、中断）都能安全结束的关键，
            // 它负责将所有状态与Pick Up And Haul同步。
            this.AddFinishAction(jobCondition => this.ReconcileStateWithPuah(jobCondition));

            // 设置 Job 级别的失败条件
            this.FailOn(() => TransporterUtility.WasLoadingCanceled(job.targetB.Thing));
            this.FailOnDestroyedOrNull(TargetIndex.B);

            // NOTE: 我们在这里获取transporter和managedLoadable，是因为它们在多个Toil中都需要被访问，
            // 在JobDriver层面进行一次性初始化可以避免在每个Toil中重复获取。
            var transporter = job.targetB.Thing.TryGetComp<CompTransporter>();
            if (transporter == null) { yield break; }
            IManagedLoadable managedLoadable = new LoadTransportersAdapter(transporter);

            // 注册周期性的“巡航修正”检查。
            // 这个FailOn负责在小人行进途中，持续验证当前目标是否仍然有效。
            this.FailOn(() => {
                if (pawn.IsHashIntervalTick(LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().AiUpdateFrequency))
                {
                    if (!BulkLoad_Utility.ValidateAndRedirectCurrentTarget(this))
                    {
                        return true;
                    }
                }
                return false;
            });

            // --- 序幕: 规划 ---
            yield return Toil_ReplanJob.Create(managedLoadable);

            // --- 检查是“拾取”还是“仅卸货” ---
            Toil pickupPhase = ToilMaker.MakeToil("PickupPhase");
            Toil unloadOnlyPhase = ToilMaker.MakeToil("UnloadOnlyPhase");
            Toil afterPickupPhase = ToilMaker.MakeToil("AfterPickupPhase");

            yield return Toils_Jump.JumpIf(unloadOnlyPhase, () => job.haulOpportunisticDuplicates);

            // =========================================================================
            //                         第一幕: 拾取流程
            // =========================================================================
            yield return pickupPhase;

            Toil pickupLoopStart = ToilMaker.MakeToil("PickupLoopStart");

            yield return pickupLoopStart;
            // 如果队列为空，直接跳转到拾取结束
            yield return Toils_Jump.JumpIf(afterPickupPhase, () => job.targetQueueA.NullOrEmpty());

            yield return Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.A);

            // --- 1a. 走向目标 (智能的) ---
            yield return Toil_GotoHaulable.Create(TargetIndex.A, managedLoadable, this, pickupLoopStart);

            // --- 1b. 如果物品在容器里，先把它“丢”出来 ---
            Toil takeFromGround = ToilMaker.MakeToil("TakeFromGround");
            yield return Toils_Jump.JumpIf(takeFromGround, () => this.TargetThingA.Spawned);
            yield return Toil_DropAndTakeFromContainer.Create(TargetIndex.A, false, this);

            // --- 1c. 把现在肯定在地上的物品拿到身上 ---
            yield return takeFromGround;

            Toil takeToCarry = ToilMaker.MakeToil("TakeToCarry");
            Toil afterTake = ToilMaker.MakeToil("AfterTake");

            // 如果只剩这最后一个目标，就计划拿到手上
            yield return Toils_Jump.JumpIf(takeToCarry, () => job.targetQueueA.Count == 0);

            // 放入背包
            yield return Toil_TakeToInventory.Create(TargetIndex.A, this, managedLoadable);
            yield return Toils_Jump.Jump(afterTake);

            // 放入手上
            yield return takeToCarry;
            yield return Toil_TakeToCarry.Create(TargetIndex.A, this);

            yield return afterTake;

            // --- 1d. 回到循环开始 ---
            yield return Toils_Jump.Jump(pickupLoopStart);

            // =========================================================================
            //                         (备选)第一幕: 仅卸货流程
            // =========================================================================
            yield return unloadOnlyPhase;
            yield return Toil_PrepareToUnloadFromInventory.Create(this, managedLoadable);

            // --- 所有拾取/准备流程的汇合点 ---
            yield return afterPickupPhase;

            // =========================================================================
            //                         第二幕: 走向目的地
            // =========================================================================
            Toil gotoToil = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            gotoToil.AddPreInitAction(() => {
                BulkLoad_Utility.ValidateAndRedirectCurrentTarget(this);
            });
            yield return gotoToil;

            // =========================================================================
            //                         第三幕: 卸货流程
            // =========================================================================
            yield return Toil_ReconcileHauledState.Create(this);
            yield return Toil_BeginUnloadSession.Create();

            Toil unloadLoopStart = ToilMaker.MakeToil("UnloadLoopStart");
            Toil unloadLoopEnd = ToilMaker.MakeToil("UnloadLoopEnd");

            yield return unloadLoopStart;
            yield return Toils_Jump.JumpIf(unloadLoopEnd, () => !HauledThings.Any());
            yield return Toil_PrepareNextUnloadItem.Create();
            yield return Toils_Jump.JumpIf(unloadLoopEnd, () => pawn.carryTracker.CarriedThing == null);

            // 模拟卸货动作的视觉延迟。
            yield return Toils_General.Wait(LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>().visualUnloadDelay, TargetIndex.B);
            yield return Toil_DepositItem.Create(managedLoadable);
            yield return Toils_Jump.Jump(unloadLoopStart);

            yield return unloadLoopEnd;
            yield return Toil_EndUnloadSession.Create();
        }
    }
}