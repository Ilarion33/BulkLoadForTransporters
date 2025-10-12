// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Utils/Extensibility_Utility.cs
using BulkLoadForTransporters.Core.Adapters;
using BulkLoadForTransporters.Core.Interfaces;
using BulkLoadForTransporters.Jobs;
using PickUpAndHaul;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace BulkLoadForTransporters.Core.Utils
{
    public static class Extensibility_Utility
    {   
        
        /// <summary>
        /// 定义了一个委托，用于扫描特定类型的可装载目标。
        /// 这个委托由主模块定义，由可选的兼容性模块来实现和注册。
        /// </summary>
        /// <param name="pawn">执行扫描的小人。</param>
        /// <returns>一个 Thing 列表，代表地图上所有潜在的目标。</returns>
        public delegate IEnumerable<Thing> OpportunisticTargetScanner(Pawn pawn);

        /// <summary>
        /// 一个公共的委托列表，允许兼容性模块注册它们自己的目标扫描器。
        /// </summary>
        public static readonly List<OpportunisticTargetScanner> OpportunisticTargetScanners = new List<OpportunisticTargetScanner>();

        /// <summary>
        /// 定义了一个委托，用于将一个 Thing 转换为 IManagedLoadable 接口。
        /// </summary>
        /// <param name="thing">要转换的 Thing。</param>
        /// <returns>一个 IManagedLoadable 实例，如果可以转换的话；否则为 null。</returns>
        public delegate IManagedLoadable AdapterFactory(Thing thing, Pawn pawn);

        /// <summary>
        /// 一个公共的委托列表，允许兼容性模块注册它们自己的 Adapter 工厂。
        /// </summary>
        public static readonly List<AdapterFactory> AdapterFactories = new List<AdapterFactory>();


        // 静态构造函数，用于注册主模块自己的扫描器和工厂
        static Extensibility_Utility()
        {
            // 注册原版运输仓和传送门的扫描器
            OpportunisticTargetScanners.Add(pawn => pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Transporter));
            OpportunisticTargetScanners.Add(pawn => pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.MapPortal));

            OpportunisticTargetScanners.Add(pawn => pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
            OpportunisticTargetScanners.Add(pawn => pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));

            // 注册原版运输仓和传送门的 Adapter 工厂
            AdapterFactories.Add((t, p) => t.TryGetComp<CompTransporter>() is CompTransporter comp ? new LoadTransportersAdapter(comp) : null);
            AdapterFactories.Add((t, p) => t is MapPortal portal ? new MapPortalAdapter(portal) : null);

            AdapterFactories.Add((t, p) => (t is IConstructible constructible && !(t is Blueprint_Install))? new ConstructionSiteAdapter(constructible) : null);
        }

        /// <summary>
        /// 一个辅助工厂方法，用于根据一个“个体”目标 Thing，创建出其所属的“群体” Adapter。
        /// 这是将“个体视野”提升到“群体视野”以进行 Manager 查询的关键。
        /// </summary>
        /// <param name="thing">作为种子的个体 Thing (例如，一个具体的工地或运输仓)。</param>
        /// <param name="pawn">执行任务的小人。</param>
        /// <returns>一个代表整个群体的 IManagedLoadable 实例，如果可以创建的话；否则为 null。</returns>
        public static IManagedLoadable CreateGroupAdapterFor(Thing thing, Pawn pawn)
        {
            // 对于建设任务，我们总是创建一个新的群体 Adapter
            if (thing is IConstructible constructible)
            {
                return new ConstructionGroupAdapter(constructible, pawn);
            }

            // 对于运输仓，Adapter 本身就通过 groupID 代表了群体
            if (thing.TryGetComp<CompTransporter>() is CompTransporter comp)
            {
                return new LoadTransportersAdapter(comp);
            }

            // 对于传送门，它自身即是群体
            if (thing is MapPortal portal)
            {
                return new MapPortalAdapter(portal);
            }

            // ... 未来可以扩展其他类型

            return null;
        }
    }
}