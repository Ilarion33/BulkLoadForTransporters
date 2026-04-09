// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Toils_DeliverConstruction/Toil_EndUnloadSessionForConstruction.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Utils;
using BulkLoadForTransporters.HarmonyPatches.DeliverConstruction;
using BulkLoadForTransporters.Jobs;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_DeliverConstruction
{
    public static class Toil_EndUnloadSessionForConstruction
    {
        public static Toil Create()
        {
            Toil toil = ToilMaker.MakeToil("EndUnloadSessionForConstruction_Relay");
            toil.initAction = () =>
            {
                var driver = toil.actor.jobs.curDriver as JobDriver_DeliverConstructionInBulk;
                if (driver == null) return;

                // 1. 执行常规的会话收尾工作
                driver.ReconcileStateWithPuah(JobCondition.Succeeded);
                driver._unloadTransferables = null;
                driver._unloadRemainingNeeds = null;

                // 2. 手动释放当前认领，为下一个 Job 做好准备
                //CentralLoadManager.Instance.ReleaseClaimsForPawn(driver.pawn);

                // 3. 将“接力”的复杂逻辑委托给通用的 Utility 方法
                WorkGiver_Utility.TryChainToNextConstructionJob(driver.pawn);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}