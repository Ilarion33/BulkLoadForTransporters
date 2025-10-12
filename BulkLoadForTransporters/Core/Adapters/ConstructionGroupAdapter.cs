// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Adapters/ConstructionGroupAdapter.cs
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace BulkLoadForTransporters.Core.Adapters
{
    /// <summary>
    /// Implements the "Construction Site Group" camouflage for MACRO-PLANNING.
    /// This adapter dynamically groups nearby construction sites and presents them as a single
    /// logical unit to the LoadingPlanner.
    /// </summary>
    public class ConstructionGroupAdapter : IManagedLoadable
    {
        private readonly IConstructible _primaryTarget;
        private readonly int _uniqueLoadID;
        private readonly Map _map;
        private readonly List<Thing> _jobTargets;
        private readonly List<ThingDefCountClass> _aggregatedNeededMaterials;
        private List<TransferableOneWay> _cachedTransferables;

        public List<Thing> GetJobTargets() => _jobTargets; // 让外部（如 Utility）安全地访问组成员列表

        public ConstructionGroupAdapter(IConstructible primaryTarget, Pawn pawn)
        {
            if (!(primaryTarget is Thing primaryTargetThing))
            {
                throw new ArgumentException("Adapter requires an IConstructible that is also a Thing.", nameof(primaryTarget));
            }
            this._primaryTarget = primaryTarget;
            this._map = primaryTargetThing.Map;

            var settings = LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>();
            int GridSize = settings.constructionGroupingGridSize;

            // --- 基于地块网格生成唯一 ID ---
            IntVec3 pos = primaryTargetThing.Position;
            int gridX = pos.x / GridSize;
            int gridZ = pos.z / GridSize;
            this._uniqueLoadID = Math.Abs(Gen.HashCombineInt(gridX, gridZ));

            // --- 分组逻辑现在扫描整个地块，而不是径向范围 ---
            // 这确保了从同一地块的任何点开始，生成的组都是完全相同的。
            var map = primaryTargetThing.Map;
            var gridRect = new CellRect(gridX * GridSize, gridZ * GridSize, GridSize, GridSize);

            var sitesInGrid = new HashSet<Thing>();
            // 使用 listerThingsForRect 获取区域内的所有 Thing，然后筛选，这比遍历全地图高效得多。
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint))
            {
                if (gridRect.Contains(thing.Position)) sitesInGrid.Add(thing);
            }
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
            {
                if (gridRect.Contains(thing.Position)) sitesInGrid.Add(thing);
            }

            // 对筛选出的工地应用原有的有效性检查
            this._jobTargets = sitesInGrid
                .Where(
                t => t is IConstructible constructible &&                
                t.Faction == pawn.Faction && 
                !(t is Blueprint_Install) && 
                !t.IsForbidden(pawn) &&
                !Compatibility_Utility.IsReplaceStuffFrame(t) && // For ReplaceStuff
                JobDriver_Utility.CanPawnWorkOnSite(pawn, constructible) &&
                GenConstruct.CanConstruct(t, pawn, true, false, JobDefRegistry.DeliverToConstruction))
                .ToList();

            var neededMaterialsDict = new Dictionary<ThingDef, int>();
            foreach (var target in this._jobTargets)
            {
                if (target is IConstructible constructible)
                {
                    foreach (var cost in constructible.TotalMaterialCost())
                    {
                        int needed = constructible.ThingCountNeeded(cost.thingDef);
                        if (needed > 0)
                        {
                            if (neededMaterialsDict.ContainsKey(cost.thingDef))
                                neededMaterialsDict[cost.thingDef] += needed;
                            else
                                neededMaterialsDict.Add(cost.thingDef, needed);
                        }
                    }
                }
            }
            this._aggregatedNeededMaterials = neededMaterialsDict.Select(kvp => new ThingDefCountClass(kvp.Key, kvp.Value)).ToList();
                        
        }
               

        public int GetUniqueLoadID() => _uniqueLoadID;
        public Thing GetParentThing() => _primaryTarget as Thing;
        public Map GetMap() => _map;
        public IEnumerable<ThingDefCountClass> GetThingsToLoad() => _aggregatedNeededMaterials;

        public List<TransferableOneWay> GetTransferables()
        {
            if (_cachedTransferables == null)
            {
                _cachedTransferables = new List<TransferableOneWay>();

                foreach (var need in _aggregatedNeededMaterials)
                {
                    if (need.count <= 0) continue;

                    var transferable = new TransferableOneWay();
                    int remainingCount = need.count;
                    int stackLimit = need.thingDef.stackLimit;

                    // 使用“足量内存伪造”来创建满足需求的“信标”
                    while (remainingCount > 0)
                    {
                        Thing fakeThing = ThingMaker.MakeThing(need.thingDef);
                        int amountToMake = System.Math.Min(remainingCount, stackLimit);
                        fakeThing.stackCount = amountToMake;
                        transferable.things.Add(fakeThing);
                        remainingCount -= amountToMake;
                    }

                    transferable.AdjustTo(need.count);
                    _cachedTransferables.Add(transferable);
                }
            }
            return _cachedTransferables;
        }

        public ThingOwner GetInnerContainerFor(Thing depositTarget)
        {
            return depositTarget?.TryGetInnerInteractableThingOwner();
        }

        public float GetMassCapacity() => 99999f;
        public float GetMassUsage() => 0f;

        public bool HandlesAbstractDemands => true;
    }
}