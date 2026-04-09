// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Adapters/SingleConstructionSiteAdapter.cs
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace BulkLoadForTransporters.Core.Adapters
{
    /// <summary>
    /// Implements the "Single Construction Site" camouflage for MICRO-CHECKS.
    /// This adapter presents a single, isolated construction site (Blueprint or Frame)
    /// to utilities like TryCreateUnloadFirstJob, providing real-time, non-grouped data.
    /// </summary>
    public class ConstructionSiteAdapter : IManagedLoadable
    {
        private readonly IConstructible _site;
        private readonly Thing _siteAsThing;
        private readonly Map _map;


        private ConstructionSiteAdapter(IConstructible site)
        {
            this._site = site;
            this._siteAsThing = site as Thing;
            this._map = _siteAsThing.Map;
        }

        public static ConstructionSiteAdapter TryCreate(IConstructible site, Pawn pawn)
        {
            var siteThing = site as Thing;
            if (siteThing == null || !siteThing.Spawned || siteThing.Map == null)
            {
                return null;
            }
            // 对于个体检查，我们仍然需要 CanPawnWorkOnSite
            if (!JobDriver_Utility.CanPawnWorkOnSite(pawn, site))
            {
                return null;
            }
            return new ConstructionSiteAdapter(site);
        }

        public int GetUniqueLoadID() => _siteAsThing?.thingIDNumber ?? -1;
        public Thing GetParentThing() => _siteAsThing;
        public Map GetMap() => _siteAsThing?.Map;

        public IEnumerable<ThingDefCountClass> GetThingsToLoad()
        {
            var totalCost = _site.TotalMaterialCost();
            if (totalCost == null) yield break;

            foreach (var cost in totalCost)
            {
                int neededCount = _site.ThingCountNeeded(cost.thingDef);
                if (neededCount > 0)
                {
                    yield return new ThingDefCountClass(cost.thingDef, neededCount);
                }
            }
        }

        public List<TransferableOneWay> GetTransferables()
        {
            var transferables = new List<TransferableOneWay>();
            var totalCost = _site.TotalMaterialCost();
            if (totalCost == null) return transferables;

            foreach (var cost in totalCost)
            {
                int neededCount = _site.ThingCountNeeded(cost.thingDef);
                if (neededCount > 0)
                {
                    var transferable = new TransferableOneWay();
                    int remainingCount = neededCount;
                    int stackLimit = cost.thingDef.stackLimit;

                    // 使用“足量内存伪造”来创建满足需求的“信标”
                    while (remainingCount > 0)
                    {
                        Thing fakeThing = ThingMaker.MakeThing(cost.thingDef);
                        int amountToMake = System.Math.Min(remainingCount, stackLimit);
                        fakeThing.stackCount = amountToMake;
                        transferable.things.Add(fakeThing);
                        remainingCount -= amountToMake;
                    }

                    transferable.AdjustTo(neededCount);
                    transferables.Add(transferable);
                }
            }
            return transferables;
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