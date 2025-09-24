// Copyright (c) 2025 Ilarion. All rights reserved.
//
// BulkLoadForTransporters_VehiclesCompat/Toil_DepositItemForVehicle.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using BulkLoadForTransporters.Jobs;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters_VehiclesCompat
{
    public static class Toil_DepositItemForVehicle
    {
        // 我们将记账逻辑直接内联到这个 Toil 中，因为这是唯一需要它的地方。
        // 这避免了所有复杂的、脆弱的外部补丁。
        private static readonly FieldInfo CountToTransferField = AccessTools.Field(typeof(TransferableOneWay), "countToTransfer");
        private static readonly FieldInfo EditBufferField = AccessTools.Field(typeof(Transferable), "editBuffer");
        private static readonly AccessTools.FieldRef<VehiclePawn, List<TransferableOneWay>> CargoToLoadRef =
            AccessTools.FieldRefAccess<VehiclePawn, List<TransferableOneWay>>("cargoToLoad");

        public static Toil Create(IManagedLoadable managedLoadable)
        {
            Toil toil = ToilMaker.MakeToil("DepositItemForVehicle_TargetedCleanup");
            toil.initAction = () =>
            {
                var driver = toil.actor.jobs.curDriver as JobDriver_BulkLoadBase;
                if (driver == null) return;
                Pawn pawn = toil.actor;

                DebugLogger.LogMessage(LogCategory.Toils, () => $"{pawn.LabelShort} is executing deposit into Vehicle.");
                Thing carriedThing = pawn.carryTracker.CarriedThing;
                if (carriedThing == null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"-> Toil FAILED: Target '{managedLoadable.GetParentThing()?.LabelCap}' is not a valid VehiclePawn.");
                    return;
                }

                var vehicle = managedLoadable.GetParentThing() as VehiclePawn;
                if (vehicle == null) return;

                // 新增一个变量来捕获实际转移的数量
                int actuallyDepositedCount = 0;

                DebugLogger.LogMessage(LogCategory.Toils, () => "Setting IsExecutingManagedUnload flag to TRUE.");
                BulkLoad_Utility.IsExecutingManagedUnload = true;
                try
                {
                    if (carriedThing is Pawn hauledPawn)
                    {
                        DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Carried item is a Pawn ({hauledPawn.LabelCap}). Entering special handling branch.");
                        bool depositedSuccessfullyAsPassenger = false;

                        // --- 1. 尝试将 Pawn 作为乘客放置 ---
                        if (hauledPawn.Downed)
                        {
                            DebugLogger.LogMessage(LogCategory.Toils, () => $"    - Pawn is downed. Searching for a dedicated passenger slot (HandlingType.None)...");
                            var passengerRoleHandler = vehicle.handlers.FirstOrDefault(h => h.role.HandlingTypes == HandlingType.None && h.AreSlotsAvailable);
                            if (passengerRoleHandler != null)
                            {
                                // 日志: 报告找到了合适的乘客座位，并准备尝试添加。
                                DebugLogger.LogMessage(LogCategory.Toils, () => $"    - Found suitable handler: '{passengerRoleHandler.role.label}'. Attempting to add pawn directly.");
                                if (vehicle.TryAddPawn(hauledPawn, passengerRoleHandler))
                                {
                                    depositedSuccessfullyAsPassenger = true;
                                }
                            }
                            else
                            {
                                // 报告未找到合适的乘客座位。
                                DebugLogger.LogMessage(LogCategory.Toils, () => $"    - No suitable passenger handler found for downed pawn.");
                            }
                        }

                        if (!depositedSuccessfullyAsPassenger)
                        {
                            DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Attempting general TryAddPawn for '{hauledPawn.LabelCap}'.");
                            if (vehicle.TryAddPawn(hauledPawn))
                            {
                                depositedSuccessfullyAsPassenger = true;
                            }
                        }

                        // --- 2. 精准的防御性清理 ---
                        if (depositedSuccessfullyAsPassenger)
                        {
                            DebugLogger.LogMessage(LogCategory.Toils, () => $"  - SUCCESS: Pawn '{hauledPawn.LabelCap}' added as passenger. Initiating defensive manifest cleanup.");
                            var cargoList = CargoToLoadRef(vehicle);
                            if (cargoList != null)
                            {
                                // 使用 RemoveAll 来清除所有指向该 Pawn 的重复条目。
                                int removedCount = cargoList.RemoveAll(t => t.things.Contains(hauledPawn));
                                if (removedCount > 0)
                                {
                                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Thorough Cleanup: Removed {removedCount} duplicate entries for passenger '{hauledPawn.LabelCap}' from cargoToLoad list.");
                                }
                            }
                        }
                        else
                        {
                            // 作为乘客失败，则作为货物处理。AddOrTransfer 自带记账，我们无需干预。
                            DebugLogger.LogMessage(LogCategory.Toils, () => $"  - FAILED to add as passenger. Falling back to treating '{hauledPawn.LabelCap}' as cargo.");
                            vehicle.AddOrTransfer(hauledPawn);
                        }
                    }
                    else
                    {
                        // 非 Pawn 物品，总是作为货物处理。AddOrTransfer 自带记账，我们无需干预。
                        DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Carried item '{carriedThing.LabelCap}' is not a pawn. Treating as standard cargo.");
                        actuallyDepositedCount = vehicle.AddOrTransfer(carriedThing);
                    }
                }
                finally
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => "Setting IsExecutingManagedUnload flag to FALSE in finally block.");
                    BulkLoad_Utility.IsExecutingManagedUnload = false;
                }

                // --- 3. 更新我们自己的系统状态 ---
                var remainingNeeds = driver._unloadRemainingNeeds;

                DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Notifying CentralLoadManager that {actuallyDepositedCount}x {carriedThing.def.defName} has been loaded.");
                CentralLoadManager.Instance?.Notify_ItemLoaded(pawn, managedLoadable, carriedThing.def, actuallyDepositedCount);
                if (remainingNeeds != null && remainingNeeds.ContainsKey(carriedThing.def))
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Updating session needs for {carriedThing.def.defName}. New need: {remainingNeeds[carriedThing.def] - actuallyDepositedCount}.");
                    remainingNeeds[carriedThing.def] -= actuallyDepositedCount;
                }

                DebugLogger.LogMessage(LogCategory.Toils, () => "  - Clearing pawn's carry tracker as physical transfer is complete.");
                pawn.carryTracker.innerContainer.Clear();

                var surplus = pawn.carryTracker.CarriedThing;
                if (surplus != null)
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Surplus detected: {surplus.LabelCap} (x{surplus.stackCount}). Processing...");
                    float availableMass = MassUtility.FreeSpace(pawn);
                    float massPerItem = surplus.GetStatValue(StatDefOf.Mass);
                    int countToStoreInInventory = (availableMass > 0 && massPerItem > 0) ? Mathf.FloorToInt(availableMass / massPerItem) : 0;
                    countToStoreInInventory = Mathf.Min(surplus.stackCount, countToStoreInInventory);
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"    - Pawn has {availableMass:F2}kg free space. Can store {countToStoreInInventory} of surplus in inventory.");

                    if (countToStoreInInventory > 0)
                    {
                        Thing portionForInventory = pawn.carryTracker.innerContainer.Take(surplus, countToStoreInInventory);
                        if (portionForInventory != null && pawn.inventory.innerContainer.TryAdd(portionForInventory, false))
                        {
                            DebugLogger.LogMessage(LogCategory.Toils, () => $"    - Stored {portionForInventory.LabelCap} in inventory as surplus.");
                            driver.SurplusThings.Add(portionForInventory);
                        }
                        else if (portionForInventory != null)
                        {
                            DebugLogger.LogMessage(LogCategory.Toils, () => $"    - Failed to add to inventory, dropped {portionForInventory.LabelCap} on ground.");
                            GenPlace.TryPlaceThing(portionForInventory, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                        }
                    }

                    if (pawn.carryTracker.CarriedThing != null)
                    {
                        DebugLogger.LogMessage(LogCategory.Toils, () => $"    - Remaining surplus on hand ({pawn.carryTracker.CarriedThing.LabelCap}) exceeds capacity. Dropping.");
                        pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing _);
                    }
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}