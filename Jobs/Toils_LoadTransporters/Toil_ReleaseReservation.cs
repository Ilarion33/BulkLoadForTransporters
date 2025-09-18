// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadTransporters/Toil_ReleaseReservation.cs
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_LoadTransporters
{
    /// <summary>
    /// Releases the pawn's reservation on a specified target.
    /// </summary>
    public static class Toil_ReleaseReservation
    {
        /// <summary>
        /// Creates a Toil that releases a reservation.
        /// </summary>
        /// <param name="index">The TargetIndex of the thing to release.</param>
        public static Toil Create(TargetIndex index)
        {
            Toil toil = ToilMaker.MakeToil("ReleaseReservation");
            toil.initAction = () =>
            {
                var pawn = toil.actor;
                var job = pawn.CurJob;
                var targetToRelease = job.GetTarget(index);

                if (targetToRelease.IsValid)
                {
                    // 调用游戏引擎的标准方法来安全地释放预定。
                    pawn.Map.reservationManager.Release(targetToRelease, pawn, job);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            return toil;
        }
    }
}