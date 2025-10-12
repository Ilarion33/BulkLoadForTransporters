// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/JobDriver_DeliverConstructionInBulk.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using BulkLoadForTransporters.HarmonyPatches.DeliverConstruction;
using BulkLoadForTransporters.Toils_DeliverConstruction;
using BulkLoadForTransporters.Toils_LoadTransporters;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Jobs
{
    public class JobDriver_DeliverConstructionInBulk : JobDriver_BulkLoadBase
    {
        [Unsaved]
        private ConstructionGroupAdapter _constructionGroupAdapter;
        public ConstructionGroupAdapter GroupAdapter => _constructionGroupAdapter;

        public override string GetReport()
        {
            if (job.targetB.HasThing)
            {
                return "BulkLoadForTransporters.ReportString.DeliveringTo".Translate(job.targetB.Thing.LabelShortCap);
            }
            return "BulkLoadForTransporters.ReportString.DeliveringTo_Generic".Translate();
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            if (pawn.Map == null)
            {
                this.ResetToilIndex();
                yield break;
            }

            this.AddFinishAction(jobCondition => this.ReconcileStateWithPuah(jobCondition));

            var primaryTarget = TargetB.Thing as IConstructible;
            if (primaryTarget == null) yield break;

            OptimisticHaulingController.IsInBulkPlanningPhase = true;
            try
            {
                this._constructionGroupAdapter = new ConstructionGroupAdapter(primaryTarget, this.pawn);
            }
            finally
            {
                OptimisticHaulingController.IsInBulkPlanningPhase = false;
            }


            this.FailOn(() => {
                if (pawn.pather.Moving && (_pickupPhaseCompleted || job.haulOpportunisticDuplicates))
                {
                    if (pawn.IsHashIntervalTick(LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().AiUpdateFrequency))
                    {
                        if (!JobDriver_Utility.ValidateAndRedirectConstructionTarget(this))
                        {
                            return true; // 验证失败，终止 Job
                        }
                    }
                }
                return false;
            });

            // --- 序幕: 规划 ---
            yield return Toil_ReplanJob.Create(_constructionGroupAdapter);

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
            yield return Toil_GotoHaulable.Create(TargetIndex.A, _constructionGroupAdapter, this, pickupLoopStart);
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
            yield return Toil_TakeToInventory.Create(TargetIndex.A, this, _constructionGroupAdapter);
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
            yield return Toil_PrepareToUnloadFromInventory.Create(this, _constructionGroupAdapter);

            // --- 所有拾取/准备流程的汇合点 ---
            yield return afterPickupPhase;

            // =========================================================================
            //                         第二幕: 走向目的地
            // =========================================================================
            Toil gotoToil = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            gotoToil.AddPreInitAction(() => {
                JobDriver_Utility.ValidateAndRedirectConstructionTarget(this);
            });
            yield return gotoToil;

            yield return Toil_ClearSiteIfNecessary.Create();

            // =========================================================================
            //                         第三幕: 卸货流程
            // =========================================================================
            // 确保主目标已经从蓝图变成了框架。
            yield return Toils_Construct.MakeSolidThingFromBlueprintIfNecessary(TargetIndex.B, TargetIndex.B);

            // --- 配送阶段 (与运输仓模式完全对齐) ---
            yield return Toil_ReconcileHauledState.Create(this);
            yield return Toil_BeginUnloadSessionForConstruction.Create();

            Toil unloadLoopStart = ToilMaker.MakeToil("UnloadLoopStart");
            Toil unloadLoopEnd = ToilMaker.MakeToil("UnloadLoopEnd");

            yield return unloadLoopStart;
            yield return Toils_Jump.JumpIf(unloadLoopEnd, () => !HauledThings.Any());
            yield return Toil_PrepareNextUnloadItem.Create();
            yield return Toils_Jump.JumpIf(unloadLoopEnd, () => pawn.carryTracker.CarriedThing == null);

            //yield return Toils_General.Wait(Mathf.Max(0, LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>().visualUnloadDelay - 15), TargetIndex.B);
            yield return Toils_General.Wait(2);
            yield return Toil_DepositItemForConstruction.Create(_constructionGroupAdapter);

            yield return Toils_Jump.Jump(unloadLoopStart);

            yield return unloadLoopEnd;
            yield return Toil_EndUnloadSessionForConstruction.Create();
        }
    }
}