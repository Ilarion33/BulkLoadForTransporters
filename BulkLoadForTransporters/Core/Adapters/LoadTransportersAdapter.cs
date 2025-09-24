// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Adapters/LoadTransportersAdapter.cs
using BulkLoadForTransporters.Core.Interfaces;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace BulkLoadForTransporters.Core.Adapters
{
    /// <summary>
    /// Adapts a vanilla CompTransporter to the IManagedLoadable interface.
    /// This allows the core framework to treat a group of transporters as a single, manageable loading task.
    /// </summary>
    public class LoadTransportersAdapter : IManagedLoadable
    {
        private readonly CompTransporter primaryTransporter;

        /// <summary>
        /// Initializes a new instance of the <see cref="LoadTransportersAdapter"/> class.
        /// </summary>
        /// <param name="primaryTransporter">Any CompTransporter within the target group.</param>
        public LoadTransportersAdapter(CompTransporter primaryTransporter)
        {
            this.primaryTransporter = primaryTransporter;
        }

        public Map GetMap() => primaryTransporter?.parent.Map;
        public int GetUniqueLoadID() => primaryTransporter?.groupID ?? -1;
        public Thing GetParentThing() => primaryTransporter?.parent;

        /// <summary>
        /// Gets the combined list of all items that need to be loaded into any transporter in the group.
        /// </summary>
        /// <returns>A consolidated list of TransferableOneWay items.</returns>
        public List<TransferableOneWay> GetTransferables()
        {
            if (primaryTransporter == null || primaryTransporter.parent.Map == null)
            {
                return new List<TransferableOneWay>();
            }

            var currentTransporters = primaryTransporter.TransportersInGroup(primaryTransporter.parent.Map);
            var combinedTransferables = new List<TransferableOneWay>();

            // 遍历所有组员，将它们的待办清单 (leftToLoad) 合并到一个总清单中。
            if (currentTransporters != null)
            {
                foreach (var transporter in currentTransporters)
                {
                    if (transporter != null && !transporter.parent.Destroyed && transporter.leftToLoad != null)
                    {
                        combinedTransferables.AddRange(transporter.leftToLoad);
                    }
                }
            }

            // 在数据源头进行过滤，只返回那些实际需要装载的条目 (数量 > 0)。
            return combinedTransferables.Where(tr => tr.CountToTransfer > 0).ToList();
        }

        /// <summary>
        /// Gets a summary of things to load, grouped by ThingDef.
        /// </summary>
        /// <returns>An enumerable of ThingDefCountClass summarizing the loading requirements.</returns>
        public IEnumerable<ThingDefCountClass> GetThingsToLoad()
        {
            var transferables = GetTransferables();
            if (transferables == null)
            {
                yield break;
            }

            // 将具体的需求条目按物品定义 (ThingDef) 分组并汇总数量。
            foreach (var group in transferables.Where(tr => tr.HasAnyThing).GroupBy(tr => tr.ThingDef))
            {
                yield return new ThingDefCountClass(group.Key, group.Sum(tr => tr.CountToTransfer));
            }
        }

        /// <summary>
        /// Gets the specific inner container of the exact transporter a pawn will deposit items into.
        /// </summary>
        /// <param name="depositTarget">The actual Thing (the specific transporter) the pawn has walked to.</param>
        /// <returns>The ThingOwner of the matching transporter, or null if not found.</returns>
        public ThingOwner GetInnerContainerFor(Thing depositTarget)
        {
            if (depositTarget == null) return null;

            // 如果卸货目标恰好是我们已知的这个主运输仓，就直接返回它的容器，避免全组扫描。
            if (this.primaryTransporter != null && this.primaryTransporter.parent == depositTarget)
            {
                return this.primaryTransporter.innerContainer;
            }

            var currentTransporters = primaryTransporter?.TransportersInGroup(primaryTransporter.parent.Map);
            if (currentTransporters != null)
            {
                // 如果目标不是主运输仓，则在整个组里查找那个父级 Thing 与卸货目标完全匹配的成员。
                var matchingTransporter = currentTransporters.FirstOrDefault(tr => tr.parent == depositTarget);
                if (matchingTransporter != null)
                {
                    return matchingTransporter.innerContainer;
                }
            }

            Log.Error($"[BulkLoad] Could not find a matching transporter for the deposit target {depositTarget.LabelCap} in the current group.");
            return null;
        }

        /// <summary>
        /// Gets the total mass capacity of all transporters in the group.
        /// </summary>
        public float GetMassCapacity()
        {
            var currentTransporters = primaryTransporter?.TransportersInGroup(primaryTransporter.parent.Map);
            return currentTransporters?.Sum(t => t.MassCapacity) ?? 0f;
        }

        /// <summary>
        /// Gets the total mass usage of all transporters in the group.
        /// </summary>
        public float GetMassUsage()
        {
            var currentTransporters = primaryTransporter?.TransportersInGroup(primaryTransporter.parent.Map);
            return currentTransporters?.Sum(t => t.MassUsage) ?? 0f;
        }
    }
}