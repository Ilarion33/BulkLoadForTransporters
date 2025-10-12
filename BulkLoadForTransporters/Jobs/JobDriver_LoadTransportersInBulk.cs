// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/JobDriver_LoadTransportersInBulk.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using BulkLoadForTransporters.Toils_LoadTransporters;
using RimWorld;
using System;
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
            if (pawn.Map == null)
            {
                this.ResetToilIndex();
                yield break;
            }

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
                if (pawn.pather.Moving && (_pickupPhaseCompleted || job.haulOpportunisticDuplicates))
                {
                    if (pawn.IsHashIntervalTick(LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().AiUpdateFrequency))
                    {
                        if (!JobDriver_Utility.ValidateAndRedirectCurrentTarget(this))
                        {
                            return true; // 验证失败，终止 Job
                        }
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
            afterPickupPhase.AddPreInitAction(() => this._pickupPhaseCompleted = true);

            yield return Toils_Jump.JumpIf(unloadOnlyPhase, () => job.haulOpportunisticDuplicates);

            // =========================================================================
            //                         第一幕: 拾取流程
            // =========================================================================
            yield return pickupPhase;

            this._handCollectionMode = false;
            Toil pickupLoopStart = ToilMaker.MakeToil("PickupLoopStart");

            yield return pickupLoopStart;
            yield return Toils_Jump.JumpIf(afterPickupPhase, () => job.targetQueueA.NullOrEmpty());

            yield return Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.A);

            // --- 1a. 信使识别 ---
            Toil messengerCheck = ToilMaker.MakeToil("MessengerCheck");
            messengerCheck.initAction = () =>
            {
                if (this.TargetThingA == this.pawn)
                {
                    this._handCollectionMode = true;
                }
            };
            messengerCheck.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return messengerCheck;
            // 如果是信使，它已经被处理，并且 TargetThingA 是 pawn，直接跳回循环开始
            yield return Toils_Jump.JumpIf(pickupLoopStart, () => this.TargetThingA == this.pawn);


            // --- 从这里开始，TargetThingA 保证是一个真实的目标 ---

            // --- 1b. 走向与准备 ---
            yield return Toil_GotoHaulable.Create(TargetIndex.A, managedLoadable, this, pickupLoopStart);
            Toil takeFromGround = ToilMaker.MakeToil("TakeFromGround");
            yield return Toils_Jump.JumpIf(takeFromGround, () => this.TargetThingA.Spawned);
            yield return Toil_DropAndTakeFromContainer.Create(TargetIndex.A, false, this);
            yield return takeFromGround;

            // --- 1c. 动态决策 ---
            Toil takeToCarryUpgraded = Toil_TakeToCarry.Create(TargetIndex.A, this);
            Toil afterTake = ToilMaker.MakeToil("AfterTake");

            // 条件：处于手持模式，或者是最后一个目标
            var shouldTakeToCarry = new Func<bool>(() => this._handCollectionMode || job.targetQueueA.Count == 0);

            // 如果条件不满足，则跳转到“放背包”的 Toil
            yield return Toils_Jump.JumpIf(takeToCarryUpgraded, shouldTakeToCarry);

            // “放背包”分支
            yield return Toil_TakeToInventory.Create(TargetIndex.A, this, managedLoadable);
            yield return Toils_Jump.Jump(afterTake); // 完成后跳到结尾

            // “拿手上”分支
            yield return takeToCarryUpgraded;

            // --- 1d. 汇合点与循环 ---
            yield return afterTake;
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
                JobDriver_Utility.ValidateAndRedirectCurrentTarget(this);
            });
            yield return gotoToil;

            // =========================================================================
            //                         第三幕: 卸货流程
            // =========================================================================
            yield return Toil_ReconcileHauledState.Create(this);
            yield return Toil_BeginUnloadSessionForTransporters.Create();

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