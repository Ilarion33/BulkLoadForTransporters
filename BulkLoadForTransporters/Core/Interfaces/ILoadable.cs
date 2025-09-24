// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Interfaces/ILoadable.cs
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace BulkLoadForTransporters.Core.Interfaces
{
    /// <summary>
    /// Represents the basic properties of any loadable entity, defining "what" needs to be loaded.
    /// </summary>
    public interface ILoadable
    {
        /// <summary>
        /// Gets a simplified list of required items as ThingDef and count pairs.
        /// </summary>
        IEnumerable<ThingDefCountClass> GetThingsToLoad();

        /// <summary>
        /// Gets the total mass capacity of the loadable entity.
        /// </summary>
        float GetMassCapacity();

        /// <summary>
        /// Gets the current mass usage of the loadable entity.
        /// </summary>
        float GetMassUsage();

        /// <summary>
        /// Gets the map this loadable entity is on.
        /// </summary>
        Map GetMap();

        /// <summary>
        /// Gets the detailed list of items to load as a collection of TransferableOneWay.
        /// This is the preferred method for obtaining the most precise requirements, including specific item instances.
        /// </summary>
        /// <returns>A list of TransferableOneWay if available; otherwise, null.</returns>
        List<TransferableOneWay> GetTransferables();
    }
}