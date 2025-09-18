// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/JobDriver_LoadPortalInBulk.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using BulkLoadForTransporters.Jobs.Toils_LoadPortal;
using BulkLoadForTransporters.Toils_LoadTransporters;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Jobs
{
    /// <summary>
    /// A concrete JobDriver for bulk loading items into a MapPortal (e.g., a PitGate).
    /// This class orchestrates the Toil sequence for the entire portal loading process,
    /// mirroring the behavior of JobDriver_LoadTransportersInBulk.
    /// </summary>
    public class JobDriver_LoadPortalInBulk : JobDriver_BulkLoadBase
    {
        /// <summary>
        /// Provides the specific report string for loading portals.
        /// </summary>
        public override string GetReport()
        {
            if (job.targetQueueA.NullOrEmpty())
            {
                if (job.targetB.HasThing)
                {
                    return "BulkLoadForTransporters.ReportString.LoadingPortal".Translate(job.targetB.Thing.LabelShortCap);
                }
            }
            return "BulkLoadForTransporters.ReportString.LoadingPortal_Generic".Translate();
        }

        /// <summary>
        /// Defines the sequence of Toils for the bulk portal loading job.
        /// </summary>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 注册核心清理器 (继承自基类)
            this.AddFinishAction(jobCondition => this.ReconcileStateWithPuah(jobCondition));

            // 设置 Job 级别的失败条件
            this.FailOn(() => EnterPortalUtility.WasLoadingCanceled(job.targetB.Thing));
            this.FailOnDestroyedOrNull(TargetIndex.B);

            var portal = job.targetB.Thing as MapPortal;
            if (portal == null) { yield break; }
            IManagedLoadable managedLoadable = new MapPortalAdapter(portal);

            this.FailOn(() => {
                if (pawn.IsHashIntervalTick(LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Settings>().AiUpdateFrequency))
                {
                    // 这里使用的是为独立目标（如Portal）设计的简化版验证逻辑。
                    if (!BulkLoad_Utility.ValidateSingleTarget(this, managedLoadable))
                    {
                        return true;
                    }
                }
                return false;
            });

            // --- 序幕: 规划 --- 
            yield return Toil_ReplanJob.Create(managedLoadable);

            // --- 检查模式 --- 
            Toil pickupPhase = ToilMaker.MakeToil("PickupPhase");
            Toil unloadOnlyPhase = ToilMaker.MakeToil("UnloadOnlyPhase");
            Toil afterPickupPhase = ToilMaker.MakeToil("AfterPickupPhase");
            yield return Toils_Jump.JumpIf(unloadOnlyPhase, () => job.haulOpportunisticDuplicates);

            // --- 第一幕: 拾取流程 ---
            yield return pickupPhase;
            Toil pickupLoopStart = ToilMaker.MakeToil("PickupLoopStart");
            yield return pickupLoopStart;
            yield return Toils_Jump.JumpIf(afterPickupPhase, () => job.targetQueueA.NullOrEmpty());
            yield return Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.A);
            yield return Toil_GotoHaulable.Create(TargetIndex.A, managedLoadable, this, pickupLoopStart);
            Toil takeFromGround = ToilMaker.MakeToil("TakeFromGround");
            yield return Toils_Jump.JumpIf(takeFromGround, () => this.TargetThingA.Spawned);
            yield return Toil_DropAndTakeFromContainer.Create(TargetIndex.A, false, this);
            yield return takeFromGround;
            Toil takeToCarry = ToilMaker.MakeToil("TakeToCarry");
            Toil afterTake = ToilMaker.MakeToil("AfterTake");
            yield return Toils_Jump.JumpIf(takeToCarry, () => job.targetQueueA.Count == 0);
            yield return Toil_TakeToInventory.Create(TargetIndex.A, this, managedLoadable);
            yield return Toils_Jump.Jump(afterTake);
            yield return takeToCarry;
            yield return Toil_TakeToCarry.Create(TargetIndex.A, this);
            yield return afterTake;
            yield return Toils_Jump.Jump(pickupLoopStart);

            // --- (备选)第一幕: 仅卸货 --- 
            yield return unloadOnlyPhase;
            yield return Toil_PrepareToUnloadFromInventory.Create(this, managedLoadable);

            // --- 汇合点 --- 
            yield return afterPickupPhase;

            // --- 第二幕: 走向目的地 --- 
            Toil gotoToil = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            gotoToil.AddPreInitAction(() => {
                BulkLoad_Utility.ValidateSingleTarget(this, managedLoadable);
            });
            yield return gotoToil;

            // --- 第三幕: 卸货流程 --- 
            yield return Toil_ReconcileHauledState.Create(this);
            yield return Toil_BeginUnloadSessionForPortal.Create();
            Toil unloadLoopStart = ToilMaker.MakeToil("UnloadLoopStart");
            Toil unloadLoopEnd = ToilMaker.MakeToil("UnloadLoopEnd");
            yield return unloadLoopStart;
            yield return Toils_Jump.JumpIf(unloadLoopEnd, () => !HauledThings.Any());
            yield return Toil_PrepareNextUnloadItem.Create();
            yield return Toils_Jump.JumpIf(unloadLoopEnd, () => pawn.carryTracker.CarriedThing == null);

            yield return Toils_General.Wait(LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>().visualUnloadDelay, TargetIndex.B);
            foreach (var toil in Toil_DepositItemForPortal.Create(managedLoadable))
            {
                yield return toil;
            }

            yield return Toils_Jump.Jump(unloadLoopStart);
            yield return unloadLoopEnd;
            yield return Toil_EndUnloadSession.Create();
        }
    }
}