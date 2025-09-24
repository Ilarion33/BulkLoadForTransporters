// Copyright (c) 2025 Ilarion. All rights reserved.
//
// BulkLoadForTransporters_VehiclesCompat/JobDriver_LoadVehicleInBulk.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using BulkLoadForTransporters.Toils_LoadTransporters;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Vehicles;
using BulkLoadForTransporters.Toils_LoadPortal;
using BulkLoadForTransporters.Jobs;

namespace BulkLoadForTransporters_VehiclesCompat
{
    /// <summary>
    /// The primary JobDriver for bulk loading Vehicles.
    /// It orchestrates a sequence of Toils to pick up multiple items and load them
    /// into a VehiclePawn, respecting special vehicle-specific logic like role assignment.
    /// </summary>
    public class JobDriver_LoadVehicleInBulk : JobDriver_BulkLoadBase
    {
        private VehiclePawn Vehicle => job.targetB.Thing as VehiclePawn;

        public override string GetReport()
        {
            // 提供一个清晰的状态报告
            if (job.targetB.HasThing)
            {
                return "BulkLoadForTransporters.ReportString.Loading".Translate(job.targetB.Thing.LabelShortCap);
            }
            return "BulkLoadForTransporters.ReportString.Loading_Generic".Translate();
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 关键：确保在任何情况下（成功、失败、中断）都能安全地与 Pick Up And Haul 同步状态。
            this.AddFinishAction(jobCondition => this.ReconcileStateWithPuah(jobCondition));

            // 设置 Job 级别的通用失败条件
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOn(() => Vehicle == null || !Vehicle.Spawned); // 确保载具一直存在

            // 为这个 JobDriver 的所有 Toils 创建一个共享的、一次性的 Adapter 实例。
            var vehicle = this.Vehicle;
            if (vehicle == null) yield break;
            IManagedLoadable managedLoadable = new VehicleAdapter(vehicle);

            // 注册周期性的“巡航修正”检查，确保任务目标在执行过程中依然有效。
            this.FailOn(() => {
                if (pawn.IsHashIntervalTick(LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().AiUpdateFrequency))
                {
                    if (!BulkLoad_Utility.ValidateSingleTarget(this, managedLoadable))
                    {
                        return true; // 如果目标失效，则终止 Job
                    }
                }
                return false;
            });

            // --- 序幕: 重新规划 (如果需要) ---
            yield return Toil_ReplanJob.Create(managedLoadable);

            // --- 检查是“拾取”还是“仅卸货” ---
            Toil pickupPhase = ToilMaker.MakeToil("PickupPhase");
            Toil unloadOnlyPhase = ToilMaker.MakeToil("UnloadOnlyPhase");
            Toil afterPickupPhase = ToilMaker.MakeToil("AfterPickupPhase");

            yield return Toils_Jump.JumpIf(unloadOnlyPhase, () => job.haulOpportunisticDuplicates);

            // =========================================================================
            //                         第一幕: 拾取流程 (与运输仓版本完全相同)
            // =========================================================================
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

            // =========================================================================
            //                         (备选)第一幕: 仅卸货流程
            // =========================================================================
            yield return unloadOnlyPhase;
            yield return Toil_PrepareToUnloadFromInventory.Create(this, managedLoadable);

            // --- 所有流程的汇合点 ---
            yield return afterPickupPhase;

            // =========================================================================
            //                         第二幕: 走向目的地
            // =========================================================================
            Toil gotoToil = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            gotoToil.AddPreInitAction(() => {
                BulkLoad_Utility.ValidateSingleTarget(this, managedLoadable);
            });
            yield return gotoToil;

            // =========================================================================
            //                         第三幕: 卸货流程 (使用专用 Toil)
            // =========================================================================
            yield return Toil_ReconcileHauledState.Create(this);
            yield return Toil_BeginUnloadSession.Create(managedLoadable);

            Toil unloadLoopStart = ToilMaker.MakeToil("UnloadLoopStart");
            Toil unloadLoopEnd = ToilMaker.MakeToil("UnloadLoopEnd");

            yield return unloadLoopStart;
            yield return Toils_Jump.JumpIf(unloadLoopEnd, () => !HauledThings.Any());
            yield return Toil_PrepareNextUnloadItem.Create();
            yield return Toils_Jump.JumpIf(unloadLoopEnd, () => pawn.carryTracker.CarriedThing == null);

            yield return Toils_General.Wait(LoadedModManager.GetMod<BulkLoadForTransporters.Core.BulkLoadForTransportersMod>().GetSettings<BulkLoadForTransporters.Core.Settings>().visualUnloadDelay, TargetIndex.B);

            // 调用为载具定制的卸货 Toil
            yield return Toil_DepositItemForVehicle.Create(managedLoadable);

            yield return Toils_Jump.Jump(unloadLoopStart);

            yield return unloadLoopEnd;
            yield return Toil_EndUnloadSession.Create();
        }
    }
}