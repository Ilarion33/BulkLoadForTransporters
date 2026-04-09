// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/CentralLoadManager.cs
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Core
{
    /// <summary>
    /// A game-wide component that tracks and manages all active bulk loading tasks.
    /// It maintains a record of what needs to be loaded, what has been claimed by pawns,
    /// and provides thread-safe methods for querying and modifying this state.
    /// </summary>
    public class CentralLoadManager : GameComponent
    {
        #region GameComponent lifecycle and data serialization

        // NOTE: This region contains boilerplate code for the GameComponent lifecycle and data serialization. 
        // It's standard RimWorld modding practice and does not contain core mod logic.
        public class ExposableIntHeadedDict<V> : IExposable where V : IExposable
        {
            public Dictionary<int, V> dict = new Dictionary<int, V>();
            public void ExposeData() { Scribe_Collections.Look(ref dict, "dict", LookMode.Value, LookMode.Deep); }
        }
        private Dictionary<Map, ExposableIntHeadedDict<LoadTaskState>> allTasks;
        private static CentralLoadManager _instance;
        public static CentralLoadManager Instance => _instance;
        public CentralLoadManager(Game game) { }
        public override void LoadedGame() { base.LoadedGame(); _instance = this; }
        public override void StartedNewGame() { base.StartedNewGame(); _instance = this; }
        public override void FinalizeInit()
        {
            base.FinalizeInit();
            _instance = this;
            if (allTasks == null)
            {
                allTasks = new Dictionary<Map, ExposableIntHeadedDict<LoadTaskState>>();
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref allTasks, "allTasks", LookMode.Reference, LookMode.Deep, ref mapKeys, ref wrapperValues);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (allTasks == null) allTasks = new Dictionary<Map, ExposableIntHeadedDict<LoadTaskState>>();
            }
        }
        private List<Map> mapKeys;
        private List<ExposableIntHeadedDict<LoadTaskState>> wrapperValues;
        private LoadTaskState GetOrCreateTaskState(IManagedLoadable task)
        {
            var map = task.GetMap();
            var taskID = task.GetUniqueLoadID();
            if (map == null || taskID < 0) return null;
            if (!allTasks.TryGetValue(map, out var mapTasksWrapper))
            {
                mapTasksWrapper = new ExposableIntHeadedDict<LoadTaskState>();
                allTasks[map] = mapTasksWrapper;
            }
            if (!mapTasksWrapper.dict.TryGetValue(taskID, out var taskState))
            {
                taskState = new LoadTaskState();
                mapTasksWrapper.dict[taskID] = taskState;
            }
            return taskState;
        }
        private LoadTaskState GetTaskState(IManagedLoadable task)
        {
            var map = task.GetMap();
            var taskID = task.GetUniqueLoadID();
            if (map == null || taskID < 0) return null;
            if (allTasks.TryGetValue(map, out var mapTasksWrapper) && mapTasksWrapper.dict.TryGetValue(taskID, out var taskState))
            {
                return taskState;
            }
            return null;
        }
        #endregion

        #region Public API (Refactored)

        /// <summary>
        /// Checks if the manager is currently tracking any loading tasks on any map.
        /// </summary>
        /// <returns>True if no loading tasks are active, false otherwise.</returns>
        public bool IsIdle()
        {
            if (allTasks == null) return true;
            return !allTasks.Values.Any(mapTasks => mapTasks.dict.Any());
        }

        /// <summary>
        /// Checks if a specific loading task is currently being tracked by the manager.
        /// </summary>
        /// <param name="task">The loading task to check, represented by its adapter.</param>
        /// <returns>True if the task is active, false otherwise.</returns>
        public bool IsTaskActive(IManagedLoadable task)
        {
            return GetTaskState(task) != null;
        }

        /// <summary>
        /// Checks if there are any items currently claimed by pawns for a specific task.
        /// This is used to determine if cargo is "on its way", even if not yet loaded.
        /// </summary>
        /// <param name="task">The loading task to check.</param>
        /// <returns>True if any items are currently claimed for this task.</returns>
        public bool AnyClaimsInProgress(IManagedLoadable task)
        {
            var taskState = GetTaskState(task);
            if (taskState == null) return false; // 任务不存在，自然没有在途认领

            // totalClaimed 聚合了所有 pawn 的认领
            return taskState.totalClaimed.Any(kvp => kvp.Value > 0);
        }

        /// <summary>
        /// Gets a set of all pawns currently assigned to any bulk loading task.
        /// This is primarily used by the SafeUnloadManager for pre-save cleanup.
        /// </summary>
        /// <returns>A HashSet of all active worker pawns.</returns>
        public HashSet<Pawn> GetAllActiveWorkers()
        {
            var workers = new HashSet<Pawn>();
            if (allTasks == null) return workers;

            foreach (var mapTasks in allTasks.Values)
            {
                foreach (var taskState in mapTasks.dict.Values)
                {
                    foreach (var pawn in taskState.pawnClaims.Keys)
                    {
                        workers.Add(pawn);
                    }
                }
            }
            return workers;
        }

        /// <summary>
        /// Notifies the manager that a map is being removed.
        /// This is a crucial cleanup step to prevent memory leaks from stale map references.
        /// Called by a Harmony patch on Game.RemoveMap.
        /// </summary>
        /// <param name="map">The map that is being removed.</param>
        public void Notify_MapRemoved(Map map)
        {
            if (allTasks != null && allTasks.ContainsKey(map))
            {
                DebugLogger.LogMessage(LogCategory.Manager, () => $"Map {map.uniqueID} is being removed. Cleaning up all associated tasks.");
                allTasks.Remove(map);
            }
        }

        /// <summary>
        /// Registers a new loading task or updates an existing one with the latest requirements.
        /// This method performs a destructive update, completely replacing the old list of needed items.
        /// </summary>
        /// <param name="task">The loading task to register or update.</param>
        public void RegisterOrUpdateTask(IManagedLoadable task)
        {
            var taskState = GetOrCreateTaskState(task);
            if (taskState == null) return;
            DebugLogger.LogMessage(LogCategory.Manager, () => $"RegisterOrUpdateTask called for task ID {task.GetUniqueLoadID()} on map {task.GetMap()?.uniqueID}.");
            taskState.UpdateTotalNeeded(task.GetTransferables());
        }

        /// <summary>
        /// Safely queries the number of each item type that are still needed and not yet claimed by any pawn.
        /// This is the primary method for the WorkGiver to determine what work is available.
        /// </summary>
        /// <param name="task">The loading task to query.</param>
        /// <returns>A read-only dictionary mapping ThingDef to the available count for claiming.</returns>
        public Dictionary<ThingDef, int> GetAvailableToClaim(IManagedLoadable task, Pawn perspectivePawn)
        {
            var taskState = GetTaskState(task);
            if (taskState == null) return new Dictionary<ThingDef, int>(); // 返回空字典，安全
            var available = taskState.GetAvailableToClaim(perspectivePawn);
            DebugLogger.LogMessage(LogCategory.Manager, () => $"GetAvailableToClaim for task ID {task.GetUniqueLoadID()}: Returning {available.Count} defs. Example: {string.Join(", ", available.Take(3).Select(kvp => $"{kvp.Key.defName}x{kvp.Value}"))}");
            return available;
        }

        /// <summary>
        /// A high-level check to see if there is any work left for a given task.
        /// It's a convenient wrapper around GetAvailableToClaim.
        /// </summary>
        /// <param name="task">The loading task to check.</param>
        /// <returns>True if there are any items available to be claimed.</returns>
        public bool HasWork(IManagedLoadable task, Pawn perspectivePawn)
        {
            return GetAvailableToClaim(task, perspectivePawn).Any(kvp => kvp.Value > 0);
        }

        /// <summary>
        /// Records that a pawn has claimed a set of items for a loading task.
        /// This is the "begin transaction" part of the loading process.
        /// </summary>
        /// <param name="pawn">The pawn claiming the items.</param>
        /// <param name="job">The job containing the hauling plan.</param>
        /// <param name="loadable">The target loading task.</param>
        public void ClaimItems(Pawn pawn, Job job, IManagedLoadable loadable)
        {
            var taskState = GetTaskState(loadable);
            if (taskState == null) { Log.Error($"[BulkLoad] CentralLoadManager.ClaimItems failed: Could not find a task state for the provided loadable."); return; }
            var plan = new Dictionary<ThingDef, int>();
            for (int i = 0; i < job.targetQueueA.Count; i++)
            {
                var thing = job.targetQueueA[i].Thing;
                if (thing == null || thing.Destroyed) continue;
                var thingDef = thing.def;
                var count = job.countQueue[i];
                if (plan.ContainsKey(thingDef)) plan[thingDef] += count;
                else plan.Add(thingDef, count);
            }
            if (plan.Any())
            {
                DebugLogger.LogMessage(LogCategory.Manager, () => $"{pawn.LabelShort} is CLAIMING items for task {loadable.GetUniqueLoadID()}. Plan: {string.Join(", ", plan.Select(kvp => $"{kvp.Key.defName}x{kvp.Value}"))}");
                taskState.AddClaim(pawn, plan);
            }
        }

        /// <summary>
        /// Notifies the manager that an item has been successfully deposited into the container.
        /// This is the "end transaction" part of the process for a single item.
        /// </summary>
        /// <param name="pawn">The pawn who deposited the item.</param>
        /// <param name="task">The target loading task.</param>
        /// <param name="depositedThing">The actual Thing instance that was deposited.</param>
        public void Notify_ItemLoaded(Pawn pawn, IManagedLoadable task, Thing depositedThing)
        {
            var taskState = GetTaskState(task);
            if (taskState == null || depositedThing == null || depositedThing.Destroyed) return;
            DebugLogger.LogMessage(LogCategory.Manager, () => $"{pawn.LabelShort} NOTIFIED loading of {depositedThing.LabelCap} (x{depositedThing.stackCount}) for task {task.GetUniqueLoadID()}.");
            taskState.SettleTransaction(pawn, depositedThing.def, depositedThing.stackCount);
        }

        /// <summary>
        /// A more generic version of Notify_ItemLoaded that does not require a live Thing instance.
        /// This is crucial for scenarios where the item is destroyed upon loading (e.g., entering a portal).
        /// </summary>
        /// <param name="pawn">The pawn who deposited the item.</param>
        /// <param name="task">The target loading task.</param>
        /// <param name="def">The ThingDef of the deposited item.</param>
        /// <param name="count">The stack count of the deposited item.</param>
        public void Notify_ItemLoaded(Pawn pawn, IManagedLoadable task, ThingDef def, int count)
        {
            var taskState = GetTaskState(task);
            if (taskState == null)
            {
                Log.Error($"[BulkLoad] CentralLoadManager.Notify_ItemLoaded failed: Could not find a task state for the provided loadable.");
                return;
            }
            DebugLogger.LogMessage(LogCategory.Manager, () => $"{pawn.LabelShort} NOTIFIED loading of {def.defName} (x{count}) for task {task.GetUniqueLoadID()} (Thing-less version).");
            taskState.SettleTransaction(pawn, def, count);
        }

        /// <summary>
        /// Releases all claims held by a specific pawn across all tasks on their map.
        /// This is the primary safety mechanism, called when a pawn's job is interrupted,
        /// they despawn, or are downed.
        /// </summary>
        /// <param name="pawn">The pawn whose claims should be released.</param>
        public void ReleaseClaimsForPawn(Pawn pawn)
        {
            DebugLogger.LogMessage(LogCategory.Manager, () => $"ReleaseClaimsForPawn triggered for {pawn.LabelShort} on map {pawn.Map?.uniqueID}.");
            // 1. 基础验证
            if (pawn == null || pawn.Map == null || allTasks == null) return;

            // 2. 验证地图任务字典是否存在
            if (!allTasks.TryGetValue(pawn.Map, out var mapTasksWrapper) || mapTasksWrapper == null)
            {
                DebugLogger.LogMessage(LogCategory.Manager, () => $"-> No tasks found on map {pawn.Map.uniqueID}. Nothing to release.");
                return;
            }

            // 3. [核心修复] 验证 mapTasksWrapper 内部的字典是否已初始化
            if (mapTasksWrapper.dict == null) return;

            foreach (var taskState in mapTasksWrapper.dict.Values)
            {
                // 4. 验证 taskState 实例本身
                if (taskState != null)
                {
                    // 5. 调用经过同样硬化的子方法
                    taskState.ReleasePawn(pawn);
                }
            }
        }
        #endregion

        #region Internal Private Class LoadTaskState (Refactored)

        // NOTE: This private class encapsulates the state for a single loading task (e.g., one transporter group).
        // It handles all the detailed bookkeeping of what's needed, what's claimed, and by whom.
        private class LoadTaskState : IExposable
        {
            public Dictionary<ThingDef, int> totalNeeded = new Dictionary<ThingDef, int>();
            public Dictionary<ThingDef, int> totalClaimed = new Dictionary<ThingDef, int>();
            public Dictionary<Pawn, Dictionary<ThingDef, int>> pawnClaims = new Dictionary<Pawn, Dictionary<ThingDef, int>>();

            /// <summary>
            /// A temporary, serializable Data Transfer Object (DTO) used to safely save and load the pawnClaims dictionary.
            /// This solves a common RimWorld issue where saving a Dictionary<Pawn, T> can lead to errors if a Pawn is no longer valid on load.
            /// </summary>
            private class PawnClaimData : IExposable
            {
                public Pawn pawn;
                public Dictionary<ThingDef, int> claims; 

                public PawnClaimData() { }

                public PawnClaimData(Pawn pawn, Dictionary<ThingDef, int> claims)
                {
                    this.pawn = pawn;
                    this.claims = claims;
                }

                public void ExposeData()
                {
                    Scribe_References.Look(ref pawn, "pawn");
                    Scribe_Collections.Look(ref claims, "claims", LookMode.Def, LookMode.Value);
                }
            }

            private List<PawnClaimData> _tmpPawnClaimsDataList;

            public void ExposeData()
            {
                Scribe_Collections.Look(ref totalNeeded, "totalNeeded", LookMode.Def, LookMode.Value);
                Scribe_Collections.Look(ref totalClaimed, "totalClaimed", LookMode.Def, LookMode.Value);

                // HACK: We use a multi-stage serialization process with a temporary list (_tmpPawnClaimsDataList)
                // to correctly handle saving and loading references to Pawns within the dictionary keys.
                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    _tmpPawnClaimsDataList = pawnClaims.Select(kvp => new PawnClaimData(kvp.Key, kvp.Value)).ToList();
                    Scribe_Collections.Look(ref _tmpPawnClaimsDataList, "pawnClaimsData", LookMode.Deep);
                }
                else if (Scribe.mode == LoadSaveMode.LoadingVars)
                {
                    Scribe_Collections.Look(ref _tmpPawnClaimsDataList, "pawnClaimsData", LookMode.Deep);
                }
                else if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    pawnClaims = new Dictionary<Pawn, Dictionary<ThingDef, int>>();
                    if (_tmpPawnClaimsDataList != null)
                    {
                        foreach (var data in _tmpPawnClaimsDataList)
                        {
                            if (data.pawn != null && data.claims != null)
                            {
                                pawnClaims[data.pawn] = data.claims;
                            }
                        }
                    }

                    _tmpPawnClaimsDataList = null;

                    totalNeeded = totalNeeded ?? new Dictionary<ThingDef, int>();
                    totalClaimed = totalClaimed ?? new Dictionary<ThingDef, int>();
                }
            }

            /// <summary>
            /// Updates the total list of required items for this task.
            /// </summary>
            public void UpdateTotalNeeded(List<TransferableOneWay> transferables)
            {
                var oldNeededCount = totalNeeded.Count;
                totalNeeded.Clear();
                if (transferables == null) return;
                foreach (var tr in transferables)
                {
                    //if (tr.AnyThing is Pawn) continue;//忽略Pawn

                    if (tr.CountToTransfer > 0 && tr.HasAnyThing)
                    {
                        if (totalNeeded.ContainsKey(tr.ThingDef)) totalNeeded[tr.ThingDef] += tr.CountToTransfer;
                        else totalNeeded.Add(tr.ThingDef, tr.CountToTransfer);
                    }
                }
                DebugLogger.LogMessage(LogCategory.Manager, () => $"  - Task state updated. Total needed defs changed from {oldNeededCount} to {totalNeeded.Count}. Example: {string.Join(", ", totalNeeded.Take(3).Select(kvp => $"{kvp.Key.defName}x{kvp.Value}"))}");
            }

            /// <summary>
            /// Adds a pawn's claim to the task's state.
            /// </summary>
            public void AddClaim(Pawn pawn, Dictionary<ThingDef, int> newPlan)
            {
                // 1. 获取该 pawn 已有的旧认领 (如果存在)
                pawnClaims.TryGetValue(pawn, out var oldClaims);
                oldClaims = oldClaims ?? new Dictionary<ThingDef, int>();

                // 2. 计算需要更新的 ThingDef 的并集
                var allDefsToUpdate = new HashSet<ThingDef>(oldClaims.Keys);
                allDefsToUpdate.AddRange(newPlan.Keys);

                // 3. 计算差值，并原子化地更新总认领 totalClaimed
                foreach (var def in allDefsToUpdate)
                {
                    int oldValue = oldClaims.TryGetValue(def, 0);
                    int newValue = newPlan.TryGetValue(def, 0);
                    int delta = newValue - oldValue;

                    if (delta != 0)
                    {
                        int currentTotal = totalClaimed.TryGetValue(def, 0);
                        int newTotal = currentTotal + delta;

                        if (newTotal > 0)
                        {
                            totalClaimed[def] = newTotal;
                        }
                        else
                        {
                            totalClaimed.Remove(def);
                        }
                    }
                }

                // 4. 用新计划完全覆盖该 pawn 的个人认领记录
                if (newPlan.Any())
                {
                    pawnClaims[pawn] = new Dictionary<ThingDef, int>(newPlan);
                }
                else
                {
                    pawnClaims.Remove(pawn);
                }

                if (newPlan.Any())
                {
                    var firstItem = newPlan.First();
                    DebugLogger.LogMessage(LogCategory.Manager, () => $"  - Claim UPDATED for {pawn.LabelShort}. New plan: {string.Join(", ", newPlan.Select(kvp => $"{kvp.Key.defName}x{kvp.Value}"))}. Total claimed for {firstItem.Key.defName} is now {totalClaimed.TryGetValue(firstItem.Key, 0)}.");
                }
                else
                {
                    DebugLogger.LogMessage(LogCategory.Manager, () => $"  - Claim CLEARED for {pawn.LabelShort} due to empty new plan.");
                }
            }

            /// <summary>
            /// Settles the transaction when an item is deposited, adjusting total and pawn-specific claims.
            /// </summary>
            public void SettleTransaction(Pawn pawn, ThingDef def, int depositedCount)
            {
                DebugLogger.LogMessage(LogCategory.Manager, () => $"  - Settling transaction for {pawn.LabelShort}: {def.defName} x{depositedCount}.");
                int oldTotalClaimed = totalClaimed.TryGetValue(def, 0);

                pawnClaims.TryGetValue(pawn, out var claims);
                int currentPawnClaim = (claims != null && claims.ContainsKey(def)) ? claims[def] : 0;
                int delta = depositedCount - currentPawnClaim;
                if (delta > 0)
                {
                    if (totalClaimed.ContainsKey(def)) totalClaimed[def] += delta;
                    else totalClaimed[def] = delta;
                }
                if (totalClaimed.ContainsKey(def))
                {
                    totalClaimed[def] = System.Math.Max(0, totalClaimed[def] - depositedCount);
                    if (totalClaimed[def] == 0) totalClaimed.Remove(def);
                }
                DebugLogger.LogMessage(LogCategory.Manager, () => $"    - Total claimed for {def.defName} changed from {oldTotalClaimed} to {totalClaimed.TryGetValue(def, 0)}.");
                if (claims != null)
                {
                    if (claims.ContainsKey(def))
                    {
                        claims[def] = System.Math.Max(0, claims[def] - depositedCount);
                        if (claims[def] == 0) claims.Remove(def);
                    }
                    if (!claims.Any())
                    {
                        pawnClaims.Remove(pawn);
                        DebugLogger.LogMessage(LogCategory.Manager, () => $"    - All claims for {pawn.LabelShort} settled. Pawn removed from claims list.");
                    }
                }
            }

            /// <summary>
            /// Releases all claims for a specific pawn, returning the claimed amounts to the available pool.
            /// </summary>
            public void ReleasePawn(Pawn pawn)
            {
                if (pawnClaims == null) return;
                if (pawnClaims.TryGetValue(pawn, out var claimsToRelease) && claimsToRelease != null)
                {
                    DebugLogger.LogMessage(LogCategory.Manager, () => $"  - Releasing {claimsToRelease.Count} claim types for {pawn.LabelShort}.");
                    if (totalClaimed != null)
                    {
                        foreach (var kvp in claimsToRelease)
                        {
                            int oldTotalClaimed = totalClaimed.TryGetValue(kvp.Key, 0);
                            if (totalClaimed.ContainsKey(kvp.Key))
                            {
                                totalClaimed[kvp.Key] -= kvp.Value;
                                if (totalClaimed[kvp.Key] <= 0) totalClaimed.Remove(kvp.Key);
                            }
                            DebugLogger.LogMessage(LogCategory.Manager, () => $"    - Releasing {kvp.Key.defName} x{kvp.Value}. Total claimed changed from {oldTotalClaimed} to {totalClaimed.TryGetValue(kvp.Key, 0)}.");
                        }
                    }
                    pawnClaims.Remove(pawn);
                }
            }

            /// <summary>
            /// Gets a snapshot of the currently available items to claim for this task.
            /// </summary>
            public Dictionary<ThingDef, int> GetAvailableToClaim(Pawn pawnToExclude = null)
            {
                var available = new Dictionary<ThingDef, int>();

                Dictionary<ThingDef, int> claimsByOthers;

                if (pawnToExclude != null && pawnClaims.TryGetValue(pawnToExclude, out var excludedPawnClaims))
                {
                    // 如果需要排除某个 pawn，我们从总认领中减去他的部分
                    claimsByOthers = new Dictionary<ThingDef, int>(totalClaimed);
                    foreach (var claim in excludedPawnClaims)
                    {
                        if (claimsByOthers.ContainsKey(claim.Key))
                        {
                            claimsByOthers[claim.Key] -= claim.Value;
                            if (claimsByOthers[claim.Key] <= 0)
                            {
                                claimsByOthers.Remove(claim.Key);
                            }
                        }
                    }
                }
                else
                {
                    claimsByOthers = totalClaimed;
                }

                // 计算使用“其他人的认领”
                foreach (var kvp in totalNeeded)
                {
                    claimsByOthers.TryGetValue(kvp.Key, out int claimedByOthersCount);
                    int availableCount = kvp.Value - claimedByOthersCount;
                    if (availableCount > 0)
                    {
                        available[kvp.Key] = availableCount;
                    }
                }
                return available;
            }
            #endregion

        }
    }
}