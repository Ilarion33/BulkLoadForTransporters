// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/JobDriver_BulkLoadBase.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Jobs
{
    /// <summary>
    /// An abstract base class for all bulk loading and unloading JobDrivers.
    /// It implements the IBulkHaulState interface to manage the logical state of hauled items
    /// and provides reusable core functionalities like saving/loading state and reconciling with Pick Up And Haul.
    /// </summary>
    public abstract class JobDriver_BulkLoadBase : JobDriver, IBulkHaulState
    {
        private static readonly FieldInfo CurToilIndexField = AccessTools.Field(typeof(JobDriver), "curToilIndex");

        #region Fields & State (from IBulkHaulState)

        protected bool _pickupPhaseCompleted = false;
        protected bool _handCollectionMode = false;

        // 记录最初从 Pick Up And Haul 的库存中“借用”的物品。
        private List<Thing> _thingsOriginallyFromPuah = new List<Thing>();

        // 本次任务中，所有被小人拾取并意图装载的物品的逻辑列表。
        private List<Thing> _hauledThingsForThisJob = new List<Thing>();

        /// <summary>
        /// Gets the list of all Things that have been hauled for the current job.
        /// This includes items in the pawn's inventory and the item being carried.
        /// </summary>
        public List<Thing> HauledThings => _hauledThingsForThisJob;

        // 在卸货过程中，因为目标容器已满或需求变更而产生的剩余物品。
        private List<Thing> _surplusThings = new List<Thing>();

        /// <summary>
        /// Gets the list of surplus Things that were not unloaded because the target was full or the requirements changed.
        /// </summary>
        public List<Thing> SurplusThings => _surplusThings;

        // 用于卸货循环的会话状态，在任务开始时不保存。
        // 它缓存了当前卸货目标的“个体视野”下的需求清单。
        [Unsaved]
        public List<TransferableOneWay> _unloadTransferables = null;
        [Unsaved]
        public Dictionary<ThingDef, int> _unloadRemainingNeeds = null;

        /// <summary>
        /// Adds a Thing to the logical list of hauled items for this job.
        /// </summary>
        public void AddHauledThing(Thing t) { if (t != null && !_hauledThingsForThisJob.Contains(t)) _hauledThingsForThisJob.Add(t); }

        /// <summary>
        /// Removes a Thing from the logical list of hauled items.
        /// </summary>
        public void RemoveHauledThing(Thing t) => _hauledThingsForThisJob.Remove(t);

        /// <summary>
        /// Tracks an item that was originally part of Pick Up And Haul's inventory.
        /// </summary>
        public void TrackOriginalPuahItem(Thing t) { if (t != null && !_thingsOriginallyFromPuah.Contains(t)) _thingsOriginallyFromPuah.Add(t); }
        #endregion

        /// <summary>
        /// A protected utility to safely reset the internal Toil index of the JobDriver.
        /// This is crucial for handling state inconsistencies during game loading.
        /// </summary>
        protected void ResetToilIndex()
        {
            if (CurToilIndexField != null)
            {
                CurToilIndexField.SetValue(this, -1);
            }
            else
            {
                Log.Error("[BulkLoad] Failed to reflect JobDriver.curToilIndex. State reset might fail.");
            }
        }

        #region Reusable Overrides & Finalizer
        /// <summary>
        /// Handles saving and loading the state of this JobDriver.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref _hauledThingsForThisJob, "hauledThingsForThisJob", LookMode.Reference);
            Scribe_Collections.Look(ref _surplusThings, "surplusThings", LookMode.Reference);
            Scribe_Collections.Look(ref _thingsOriginallyFromPuah, "thingsOriginallyFromPuah", LookMode.Reference);
            Scribe_Values.Look(ref _pickupPhaseCompleted, "pickupPhaseCompleted", false);
            Scribe_Values.Look(ref _handCollectionMode, "handCollectionMode", false);
        }

        /// <summary>
        /// Reserves all haulable targets at the beginning of the job.
        /// </summary>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (job.targetQueueA != null && job.targetQueueA.Any())
            {
                pawn.ReserveAsManyAsPossible(job.targetQueueA, job);
            }
            return true;
        }


        /// <summary>
        /// The core cleanup logic that runs when the job ends for any reason (success, failure, interrupt).
        /// It ensures that any items remaining in the pawn's possession are correctly registered back with Pick Up And Haul.
        /// </summary>
        public void ReconcileStateWithPuah(JobCondition jobCondition)
        {
            var puahComp = pawn.TryGetComp<PickUpAndHaul.CompHauledToInventory>();
            if (puahComp == null) return;

            var puahSet = puahComp.GetHashSet();
            foreach (var originalThing in _thingsOriginallyFromPuah)
            {
                puahSet.Remove(originalThing);
            }

            var allRemainingThings = SurplusThings.Concat(HauledThings)
                .Where(thing => thing != null && !thing.Destroyed &&
                               (pawn.inventory.innerContainer.Contains(thing) || pawn.carryTracker.CarriedThing == thing));

            // 调用通用的工具方法来执行注册
            Global_Utility.RegisterHauledThingsWithPuah(pawn, allRemainingThings);

            HauledThings.Clear();
            SurplusThings.Clear();
            _thingsOriginallyFromPuah.Clear();
        }

        /// <summary>
        /// Provides the status string that appears in the pawn's inspection pane.
        /// This must be implemented by the concrete subclass.
        /// </summary>
        public override abstract string GetReport();

        public override bool IsContinuation(Job newJob)
        {
            // 检查当前 Job 和新 Job 是否都是我们系统内的 Job
            bool isCurrentJobManaged = JobDefRegistry.IsLoadingJob(this.job.def) || JobDefRegistry.IsUnloadingJob(this.job.def);
            bool isNewJobManaged = newJob != null && (JobDefRegistry.IsLoadingJob(newJob.def) || JobDefRegistry.IsUnloadingJob(newJob.def));

            if (isCurrentJobManaged && isNewJobManaged)
            {
                // 如果它们的目标 (运输仓组或传送门) 是同一个，就视为连续
                return this.job.targetB == newJob.targetB;
            }

            return base.IsContinuation(newJob);
        }

        /// <summary>
        /// The main method where the sequence of Toils for the job is defined.
        /// This must be implemented by the concrete subclass.
        /// </summary>
        protected override abstract IEnumerable<Toil> MakeNewToils();

        #endregion
    }
}