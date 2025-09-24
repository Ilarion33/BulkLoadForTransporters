// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Jobs/Toils_LoadTransporters/Toil_GotoHaulable.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Toils_LoadTransporters
{
    /// <summary>
    /// A smart "goto" Toil that can target a Thing directly or the container it is in.
    /// </summary>
    public static class Toil_GotoHaulable
    {
        /// <summary>
        /// Creates the Toil.
        /// </summary>
        /// <param name="index">The TargetIndex of the haulable Thing.</param>
        /// <param name="loadable">The context of the overall loading job.</param>
        /// <param name="haulState">The JobDriver's state tracker.</param>
        /// <param name="jumpTarget">The Toil to jump to if the target becomes invalid.</param>
        public static Toil Create(TargetIndex index, ILoadable loadable, IBulkHaulState haulState, Toil jumpTarget)
        {
            Toil toil = ToilMaker.MakeToil("GotoHaulable");
            int ticksUntilNextCheck = 0;
            bool needsToEnd = false;

            toil.initAction = () =>
            {
                var pawn = toil.actor;
                var thing = pawn.CurJob.GetTarget(index).Thing;
                DebugLogger.LogMessage(LogCategory.Toils, () => $"{pawn.LabelShort} is executing GotoHaulable for '{thing?.LabelCap}'.");

                // 基础有效性检查
                if (thing == null || thing.Destroyed || thing.IsForbidden(pawn))
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"-> Toil JUMPING: Target is null, destroyed, or forbidden.");
                    pawn.jobs.curDriver.JumpToToil(jumpTarget);
                    return;
                }

                // 这是解决“书架问题”的核心逻辑。
                LocalTargetInfo destination;
                if (thing.Spawned)
                {
                    destination = thing;
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Target is spawned. Destination set to the thing itself.");
                    // 只有在物品真的在地图上时，我们才能检查预定状态。
                    if (!pawn.CanReserve(thing))
                    {
                        DebugLogger.LogMessage(LogCategory.Toils, () => $"-> Toil JUMPING: Cannot reserve spawned target.");
                        pawn.jobs.curDriver.JumpToToil(jumpTarget);
                        return;
                    }
                }
                else if (thing.ParentHolder is Thing container)
                {
                    // 如果物品在容器里，目标就是那个容器。
                    destination = container;
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Target is in container '{container.LabelCap}'. Destination set to container.");
                }
                else
                {
                    DebugLogger.LogMessage(LogCategory.Toils, () => $"-> Toil JUMPING: Target is not spawned and not in a valid container. Parent: {thing.ParentHolder?.ToString()}.");
                    // 既不在地上也不在Thing容器里，视为无效目标
                    pawn.jobs.curDriver.JumpToToil(jumpTarget);
                    return;
                }

                // 启动寻路
                pawn.pather.StartPath(destination, PathEndMode.ClosestTouch);
                ticksUntilNextCheck = LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Core.Settings>().AiUpdateFrequency;
                needsToEnd = false;
                DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Pathing started towards {destination}.");
            };

            toil.tickAction = () =>
            {
                // 周期性地重新验证当前任务是否仍然有必要。
                // 例如，如果其他小人已经满足了我们正要去拿的这种物品的需求。
                ticksUntilNextCheck--;
                if (ticksUntilNextCheck <= 0)
                {
                    Thing thing = toil.actor.CurJob.GetTarget(index).Thing;
                    int neededAmount = (thing != null) ? Toil_TakeToInventory.GetCurrentNeededAmountFor(loadable, thing.def, haulState) : 0;
                    if (thing == null || neededAmount <= 0)
                    {
                        DebugLogger.LogMessage(LogCategory.Toils, () => $"  - Tick check: Target '{thing?.LabelCap}' is no longer needed (needed: {neededAmount}). Ending this goto.");
                    }
                    ticksUntilNextCheck = LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Core.Settings>().AiUpdateFrequency;
                }
            };

            // 如果 tickAction 将 needsToEnd 设为 true，这个 Toil 会成功结束，让 JobDriver 继续到下一个 Toil，
            // 通常是跳回到循环开始，以提取下一个目标。
            toil.AddEndCondition(() => needsToEnd ? JobCondition.Succeeded : JobCondition.Ongoing);
            toil.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            toil.FailOnDestroyedOrNull(index);
            toil.FailOnForbidden(index);
            toil.FailOnSomeonePhysicallyInteracting(index);
            toil.FailOnBurningImmobile(index);
            return toil;
        }
    }
}