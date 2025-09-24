// Copyright (c) 2025 Ilarion. All rights reserved.
//
// BulkLoadForTransporters_VehiclesCompat/VehicleAdapter.cs
using BulkLoadForTransporters.Core.Interfaces;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Vehicles;

namespace BulkLoadForTransporters_VehiclesCompat
{
    /// <summary>
    /// Adapts a VehiclePawn to the IManagedLoadable interface.
    /// This allows the core framework to treat a vehicle as a manageable loading task.
    /// </summary>
    public class VehicleAdapter : IManagedLoadable
    {
        private readonly VehiclePawn vehicle;

        private static readonly AccessTools.FieldRef<VehiclePawn, List<TransferableOneWay>> CargoToLoadRef =
            AccessTools.FieldRefAccess<VehiclePawn, List<TransferableOneWay>>("cargoToLoad");

        public VehicleAdapter(VehiclePawn vehicle)
        {
            if (vehicle == null) throw new System.ArgumentNullException(nameof(vehicle));
            this.vehicle = vehicle;
        }

        public Map GetMap() => vehicle.Map;
        public int GetUniqueLoadID() => vehicle.thingIDNumber;
        public Thing GetParentThing() => vehicle;

        public List<TransferableOneWay> GetTransferables()
        {
            var transferables = CargoToLoadRef(vehicle);
            return transferables?.Where(tr => tr.CountToTransfer > 0).ToList() ?? new List<TransferableOneWay>();
        }

        public ThingOwner GetInnerContainerFor(Thing depositTarget)
        {
            if (depositTarget == vehicle && vehicle.inventory != null)
            {
                return vehicle.inventory.innerContainer;
            }
            return null;
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

        public float GetMassCapacity()
        {
            return MassUtility.Capacity(vehicle);
        }

        public float GetMassUsage()
        {
            if (vehicle.inventory?.innerContainer == null)
            {
                return 0f;
            }
            return vehicle.inventory.innerContainer.Sum(thing => thing.GetStatValue(StatDefOf.Mass) * thing.stackCount);
        }
    }
}