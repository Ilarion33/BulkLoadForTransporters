// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Interfaces/IBulkHaulState.cs
using System.Collections.Generic;
using Verse;

namespace BulkLoadForTransporters.Core.Interfaces
{
    /// <summary>
    /// Defines the contract for a JobDriver's internal state during a bulk hauling operation.
    /// This allows different Toils to access and modify a shared state for a single job.
    /// </summary>
    public interface IBulkHaulState
    {
        /// <summary>
        /// A list of all things the pawn has picked up from the map or its inventory for this specific job.
        /// This is the master list of items to be unloaded.
        /// </summary>
        List<Thing> HauledThings { get; }

        /// <summary>
        /// A list of items that were left over after the unloading process (e.g., due to the loading list changing mid-job).
        /// </summary>
        List<Thing> SurplusThings { get; }

        /// <summary>
        /// Adds a thing to the HauledThings list.
        /// </summary>
        void AddHauledThing(Thing t);

        /// <summary>
        // Removes a thing from the HauledThings list.
        /// </summary>
        void RemoveHauledThing(Thing t);

        /// <summary>
        /// Records an item that was originally part of the Pick Up And Haul inventory.
        /// This is used for correct reconciliation with PUAH's state at the end of the job.
        /// </summary>
        void TrackOriginalPuahItem(Thing t);
    }
}