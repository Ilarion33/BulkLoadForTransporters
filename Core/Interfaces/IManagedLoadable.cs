// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Interfaces/IManagedLoadable.cs
using RimWorld;
using Verse;

namespace BulkLoadForTransporters.Core.Interfaces
{
    /// <summary>
    /// Represents a multi-pawn loading task that can be tracked by the CentralLoadManager.
    /// It extends ILoadable with contracts for unique identification and container access,
    /// enabling coordinated, concurrent hauling.
    /// </summary>
    public interface IManagedLoadable : ILoadable
    {
        /// <summary>
        /// Gets a stable, map-unique ID for this loading task.
        /// This is crucial for the CentralLoadManager to track ongoing progress.
        /// </summary>
        /// <returns>A unique integer ID, or a value less than 0 if the task is invalid.</returns>
        int GetUniqueLoadID();

        /// <summary>
        /// Gets the specific ThingOwner to deposit items into, based on the final destination pawn walks to.
        /// </summary>
        /// <param name="depositTarget">The actual Thing the pawn is standing next to when unloading (e.g., a specific transport pod).</param>
        /// <returns>The ThingOwner for depositing, or null if invalid.</returns>
        ThingOwner GetInnerContainerFor(Thing depositTarget);

        /// <summary>
        /// Gets the primary parent Thing associated with this loading task.
        /// Used for pathfinding, setting job targets, and other common interactions.
        /// </summary>
        Thing GetParentThing();
    }
}