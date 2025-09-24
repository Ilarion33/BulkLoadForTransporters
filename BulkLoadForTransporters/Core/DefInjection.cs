// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/DefInjection.cs
using BulkLoadForTransporters.Core.Components;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace BulkLoadForTransporters.Core
{
    /// <summary>
    /// Handles the dynamic injection of CompProperties into ThingDefs upon game startup.
    /// This allows the mod to attach its functionality to various vanilla, DLC, or mod-added
    /// things like transport pods and portals without needing explicit XML patches for each one.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class DefInjection
    {
        static DefInjection()
        {
            // NOTE: 这些是CompProperties的实例，将被注入到目标ThingDef中。
            // 预先创建它们可以避免在循环中重复实例化。
            var bulkLoadableTransporter = new CompProperties_BulkLoadable();
            var bulkLoadablePortal = new CompProperties_BulkLoadablePortal();

            // 遍历游戏数据库中所有的 ThingDef
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                // 通过检查是否存在CompTransporter来识别所有类型的运输仓（原版、DLC、Mod）。
                if (def.HasComp(typeof(CompTransporter)))
                {
                    TryInjectComp(def, bulkLoadableTransporter);
                    continue; 
                }

                // 通过检查thingClass是否继承自MapPortal来识别所有类型的传送门。
                if (def.thingClass != null && typeof(MapPortal).IsAssignableFrom(def.thingClass))
                {
                    TryInjectComp(def, bulkLoadablePortal);
                }
            }
        }

        /// <summary>
        /// Safely injects a CompProperties into a ThingDef's comp list.
        /// It ensures the comp list is initialized and prevents duplicate injections.
        /// </summary>
        /// <param name="def">The ThingDef to inject into.</param>
        /// <param name="compToAdd">The CompProperties instance to add.</param>
        private static void TryInjectComp(ThingDef def, CompProperties compToAdd)
        {
            // 如果目标Def已经有了同类型的Comp，则跳过以防止重复注入。
            if (def.comps.Any(c => c.GetType() == compToAdd.GetType()))
            {
                return;
            }

            // 确保comps列表存在，如果为null则初始化。
            if (def.comps == null)
            {
                def.comps = new List<CompProperties>();
            }

            def.comps.Add(compToAdd);
        }
    }
}