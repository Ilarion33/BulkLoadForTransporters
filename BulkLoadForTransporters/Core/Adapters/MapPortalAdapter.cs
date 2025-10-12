// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Adapters/MapPortalAdapter.cs
using BulkLoadForTransporters.Core.Interfaces;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace BulkLoadForTransporters.Core.Adapters
{
    /// <summary>
    /// Adapts a vanilla MapPortal to the IManagedLoadable interface.
    /// This allows the core framework to treat a portal as a manageable loading task.
    /// </summary>
    public class MapPortalAdapter : IManagedLoadable
    {
        private readonly MapPortal primaryPortal;

        public MapPortalAdapter(MapPortal primaryPortal)
        {
            this.primaryPortal = primaryPortal;
        }

        public Map GetMap() => primaryPortal?.Map;

        // 使用MapPortal的 thingIDNumber 作为唯一标识。
        public int GetUniqueLoadID() => primaryPortal?.thingIDNumber ?? -1;

        public Thing GetParentThing() => primaryPortal;

        /// <summary>
        /// Gets the list of items that need to be loaded into this specific portal.
        /// </summary>
        /// <returns>A list of TransferableOneWay items for this portal.</returns>
        public List<TransferableOneWay> GetTransferables()
        {
            // MapPortal 的待办清单是直接的，不需要像 Transporter 那样合并组。
            if (primaryPortal == null || primaryPortal.Destroyed || primaryPortal.leftToLoad == null)
            {
                return new List<TransferableOneWay>();
            }

            return primaryPortal.leftToLoad.Where(tr => tr.CountToTransfer > 0).ToList();
        }

        public IEnumerable<ThingDefCountClass> GetThingsToLoad()
        {
            var transferables = GetTransferables();
            if (transferables == null)
            {
                yield break;
            }

            foreach (var group in transferables.Where(tr => tr.HasAnyThing).GroupBy(tr => tr.ThingDef))
            {
                yield return new ThingDefCountClass(group.Key, group.Sum(tr => tr.CountToTransfer));
            }
        }

        /// <summary>
        /// Gets the specific container proxy for the portal that will handle the teleportation.
        /// </summary>
        /// <param name="depositTarget">The portal itself.</param>
        /// <returns>The portal's container proxy (PortalContainerProxy).</returns>
        public ThingOwner GetInnerContainerFor(Thing depositTarget)
        {
            // 对于 Portal，卸货目标就是它自己。
            if (depositTarget is MapPortal portal)
            {
                // 调用 GetDirectlyHeldThings() 来获取那个特殊的 PortalContainerProxy。
                return portal.GetDirectlyHeldThings();
            }

            Log.Error($"[BulkLoad] MapPortalAdapter: Deposit target {depositTarget?.LabelCap} is not a MapPortal.");
            return null;
        }

        /// <summary>
        /// Gets the mass capacity of the portal.
        /// </summary>
        /// <returns>Effectively infinite, as portals have no mass limit.</returns>
        public float GetMassCapacity()
        {
            // MapPortal 没有质量限制。返回一个极大的值来表示“无限容量”。
            return float.MaxValue;
        }

        public float GetMassUsage()
        {
            // MapPortal 没有质量使用的概念。
            return 0f;
        }

        public bool HandlesAbstractDemands => false;
    }
}