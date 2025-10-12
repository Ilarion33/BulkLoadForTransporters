// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Components/CompBulkLoadable.cs
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Core.Utils;
using BulkLoadForTransporters.Jobs;
using PickUpAndHaul;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Core.Components
{
    /// <summary>
    /// The CompProperties class required by ThingDefs to use our CompBulkLoadable.
    /// </summary>
    public class CompProperties_BulkLoadable : CompProperties
    {
        public CompProperties_BulkLoadable()
        {
            // 将这个属性类与我们的组件实现类关联起来
            this.compClass = typeof(CompBulkLoadable);
        }
    }

    /// <summary>
    /// Provides the "Prioritize bulk loading" right-click options for transporters.
    /// </summary>
    public class CompBulkLoadable : ThingComp
    {
        private int _cacheTick = -1;
        private const int CacheDurationTicks = 60;
        private Pawn _cachePawn = null;
        private readonly List<FloatMenuOption> _cachedOptions = new List<FloatMenuOption>();

        /// <summary>
        /// Called by the game engine to get right-click menu options for this Thing.
        /// </summary>
        /// <param name="pawn">The pawn that is right-clicking.</param>
        /// <returns>An enumerable of FloatMenuOption.</returns>
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
        {
            // --- 缓存检查 ---
            if (GenTicks.TicksGame < _cacheTick + CacheDurationTicks && pawn == _cachePawn)
            {
                foreach (var option in _cachedOptions)
                {
                    yield return option;
                }
                yield break;
            }

            _cachedOptions.Clear();
            _cacheTick = GenTicks.TicksGame;
            _cachePawn = pawn;

            DebugLogger.LogMessage(LogCategory.WorkGiver, () => $"Evaluating right-click options for {pawn.LabelShort} on Transporter '{parent.LabelCap}' at {parent.Position}?");
            if (!LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkLoadTransporters)
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Bulk Load Transporters feature is disabled in settings.");
                yield break; 
            }

            // 一系列的“前置资格检查”，确保只有在合法的情况下才显示菜单。
            if (pawn.Drafted)
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Pawn is drafted.");
                yield break;
            }

            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Pawn is incapable of manipulation.");
                yield break;
            }

            if (pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.Hauling) == 0)
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Hauling work type is disabled for this pawn.");
                yield break;
            }

            if (!(pawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Deadly)))
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Pawn cannot reserve and reach the target.");
                yield break;
            }

            var transporter = parent.TryGetComp<CompTransporter>();
            if (transporter == null)
            {
                yield break;
            }

            // 防止小人中断自己正在执行的、针对同一个运输仓组的任务。
            if (pawn.CurJob != null && pawn.CurJob.def == JobDefRegistry.LoadTransporters)
            {

                var jobTarget = pawn.CurJob.targetB.Thing;
                var jobTransporter = jobTarget?.TryGetComp<CompTransporter>();

                if (jobTransporter != null && jobTransporter.groupID == transporter.groupID && transporter.groupID != -1)
                {
                    yield break;
                }
            }

            if (pawn.RaceProps.IsMechanoid && !PickUpAndHaul.Settings.AllowMechanoids)
            {
                yield break;
            }

            if (CentralLoadManager.Instance == null)
            {
                yield break;
            }

            IManagedLoadable groupLoadable = new LoadTransportersAdapter(transporter);

            // 主动向中央管理器注册或更新此任务的状态，确保我们获取的是最新信息。
            CentralLoadManager.Instance.RegisterOrUpdateTask(groupLoadable);

            bool hasWorkToDo = CentralLoadManager.Instance.HasWork(groupLoadable, pawn) || WorkGiver_Utility.PawnHasNeededPuahItems(pawn, groupLoadable);
            if (!hasWorkToDo)
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: Manager reports no work to do, and pawn has no relevant items in inventory.");
                yield break;
            }

            // 检查剩余的工作中是否至少有一项是需要“搬运”的。
            var remainingTransferables = groupLoadable.GetTransferables();
            var availableToClaim = CentralLoadManager.Instance.GetAvailableToClaim(groupLoadable, pawn);

            bool anyHaulableWorkLeft = false;
            if (remainingTransferables != null)
            {
                foreach (var transferable in remainingTransferables)
                {
                    if (transferable.CountToTransfer > 0 && transferable.HasAnyThing && availableToClaim.ContainsKey(transferable.ThingDef))
                    {
                        if (transferable.AnyThing is Pawn p)
                        {
                            if (Global_Utility.NeedsToBeCarried(p))
                            {
                                anyHaulableWorkLeft = true;
                                break;
                            }
                        }
                        else
                        {
                            anyHaulableWorkLeft = true;
                            break;
                        }
                    }
                }
            }

            // 如果背包里有东西可以卸，也算是有“搬运”工作
            if (WorkGiver_Utility.PawnHasNeededPuahItems(pawn, groupLoadable))
            {
                anyHaulableWorkLeft = true;
            }

            // 如果没有任何需要“搬运”的工作剩下，就不显示右键菜单
            if (!anyHaulableWorkLeft)
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> NO: All remaining work is for self-moving pawns; no hauling job can be given.");
                yield break;
            }

            DebugLogger.LogMessage(LogCategory.WorkGiver, () => "-> YES: All checks passed. Generating FloatMenuOption(s).");
            string label = "BulkLoadForTransporters.PriorityLoadCommand".Translate(parent.LabelShortCap);

            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption(label, () =>
            {
                // 给小人一个短暂的Wait Job，以中断他当前可能正在做的任何事情。
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wait, 2), JobTag.Misc);

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    var puahComp = pawn.TryGetComp<PickUpAndHaul.CompHauledToInventory>();
                    if (puahComp != null && puahComp.GetHashSet().Any())
                    {
                        IManagedLoadable unloadAdapter = new LoadTransportersAdapter(transporter);
                        if (!WorkGiver_Utility.TryCreateDirectedUnloadJob(pawn, unloadAdapter, out _))
                        {
                            foreach (var thing in puahComp.GetHashSet().ToList())
                            {
                                pawn.inventory.innerContainer.TryDrop(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _);
                            }
                        }
                    }

                    if (WorkGiver_Utility.TryGiveBulkJob(pawn, groupLoadable, out Job job) && job.def != JobDefOf.Wait)
                    {
                        pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    }
                    else
                    {
                        // 在 LongEventHandler 中，可以安全地显示消息
                        Messages.Message("BulkLoadForTransporters.NoHaulPlanFound".Translate(pawn.LabelShort), MessageTypeDefOf.RejectInput);
                    }
                });
            }));

            // 如果在设置中启用了“连续装载”功能，则额外提供一个“直到完成”的选项。
            if (LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>().enableContinuousLoading)
            {
                DebugLogger.LogMessage(LogCategory.WorkGiver, () => "  - Also generating 'load until complete' option.");
                string continuousLabel = "BulkLoadForTransporters.PriorityLoadUntilCompleteCommand".Translate(parent.LabelShortCap);

                options.Add(new FloatMenuOption(continuousLabel, () =>
                {
                    pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wait, 2), JobTag.Misc);

                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        var puahComp = pawn.TryGetComp<PickUpAndHaul.CompHauledToInventory>();
                        if (puahComp != null && puahComp.GetHashSet().Any())
                        {
                            IManagedLoadable unloadAdapter = new LoadTransportersAdapter(transporter);
                            if (!WorkGiver_Utility.TryCreateDirectedUnloadJob(pawn, unloadAdapter, out _))
                            {
                                foreach (var thing in puahComp.GetHashSet().ToList())
                                {
                                    pawn.inventory.innerContainer.TryDrop(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _);
                                }
                            }
                        }

                        if (WorkGiver_Utility.TryGiveBulkJob(pawn, groupLoadable, out Job job) && job.def != JobDefOf.Wait)
                        {
                            // NOTE: job.loadID = 1 是用来标记“连续模式”的信号旗。
                            job.loadID = 1;
                            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                        }
                        else
                        {
                            Messages.Message("BulkLoadForTransporters.NoHaulPlanFound".Translate(pawn.LabelShort), MessageTypeDefOf.RejectInput);
                        }
                    });
                }));
            }
            // 将计算结果存入缓存
            _cachedOptions.AddRange(options);

            // 返回新计算出的结果
            foreach (var option in _cachedOptions)
            {
                yield return option;
            }
        }
    }
}
    
