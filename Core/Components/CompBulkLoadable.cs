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
        /// <summary>
        /// Called by the game engine to get right-click menu options for this Thing.
        /// </summary>
        /// <param name="pawn">The pawn that is right-clicking.</param>
        /// <returns>An enumerable of FloatMenuOption.</returns>
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
        {
            if (!LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>().enableBulkLoadTransporters)
            {
                yield break; 
            }

            // 一系列的“前置资格检查”，确保只有在合法的情况下才显示菜单。
            if (pawn.Drafted)
            {
                yield break;
            }

            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                yield break;
            }

            if (pawn.workSettings != null && pawn.workSettings.GetPriority(WorkTypeDefOf.Hauling) == 0)
            {
                yield break;
            }

            if (!(pawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Deadly)))
            {
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

            bool hasWorkToDo = CentralLoadManager.Instance.HasWork(groupLoadable) || BulkLoad_Utility.PawnHasNeededPuahItems(pawn, groupLoadable);
            if (!hasWorkToDo)
            {
                yield break;
            }

            // 检查剩余的工作中是否至少有一项是需要“搬运”的。
            var remainingTransferables = groupLoadable.GetTransferables();
            var availableToClaim = CentralLoadManager.Instance.GetAvailableToClaim(groupLoadable);

            bool anyHaulableWorkLeft = false;
            if (remainingTransferables != null)
            {
                foreach (var transferable in remainingTransferables)
                {
                    if (transferable.CountToTransfer > 0 && transferable.HasAnyThing && availableToClaim.ContainsKey(transferable.ThingDef))
                    {
                        if (transferable.AnyThing is Pawn p)
                        {
                            if (LoadTransporters_WorkGiverUtility.NeedsToBeCarried(p))
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
            if (BulkLoad_Utility.PawnHasNeededPuahItems(pawn, groupLoadable))
            {
                anyHaulableWorkLeft = true;
            }

            // 如果没有任何需要“搬运”的工作剩下，就不显示右键菜单
            if (!anyHaulableWorkLeft)
            {
                yield break;
            }

            string label = "BulkLoadForTransporters.PriorityLoadCommand".Translate(parent.LabelShortCap);

            yield return new FloatMenuOption(label, () =>
            {
                // 给小人一个短暂的Wait Job，以中断他当前可能正在做的任何事情。
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wait, 2), JobTag.Misc);

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    var puahComp = pawn.TryGetComp<PickUpAndHaul.CompHauledToInventory>();
                    if (puahComp != null && puahComp.GetHashSet().Any())
                    {
                        IManagedLoadable unloadAdapter = new LoadTransportersAdapter(transporter);
                        if (!BulkLoad_Utility.TryCreateDirectedUnloadJob(pawn, unloadAdapter, out _))
                        {
                            foreach (var thing in puahComp.GetHashSet().ToList())
                            {
                                pawn.inventory.innerContainer.TryDrop(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _);
                            }
                        }
                    }

                    if (LoadTransporters_WorkGiverUtility.TryGiveBulkJob(pawn, groupLoadable, out Job job) && job.def != JobDefOf.Wait)
                    {
                        pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    }
                    else
                    {
                        // 在 LongEventHandler 中，可以安全地显示消息
                        Messages.Message("BulkLoadForTransporters.NoHaulPlanFound".Translate(pawn.LabelShort), MessageTypeDefOf.RejectInput);
                    }
                });
            });

            // 如果在设置中启用了“连续装载”功能，则额外提供一个“直到完成”的选项。
            if (LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>().enableContinuousLoading)
            {
                string continuousLabel = "BulkLoadForTransporters.PriorityLoadUntilCompleteCommand".Translate(parent.LabelShortCap);

                yield return new FloatMenuOption(continuousLabel, () =>
                {
                    pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wait, 2), JobTag.Misc);

                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        var puahComp = pawn.TryGetComp<PickUpAndHaul.CompHauledToInventory>();
                        if (puahComp != null && puahComp.GetHashSet().Any())
                        {
                            IManagedLoadable unloadAdapter = new LoadTransportersAdapter(transporter);
                            if (!BulkLoad_Utility.TryCreateDirectedUnloadJob(pawn, unloadAdapter, out _))
                            {
                                foreach (var thing in puahComp.GetHashSet().ToList())
                                {
                                    pawn.inventory.innerContainer.TryDrop(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _);
                                }
                            }
                        }

                        if (LoadTransporters_WorkGiverUtility.TryGiveBulkJob(pawn, groupLoadable, out Job job) && job.def != JobDefOf.Wait)
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
                });
            }
        }
    }
}
    
