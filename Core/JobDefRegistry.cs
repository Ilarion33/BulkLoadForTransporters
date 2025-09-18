// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/JobDefRegistry.cs
using RimWorld;
using System.Collections.Generic; 
using Verse;

namespace BulkLoadForTransporters.Core
{
    /// <summary>
    /// A static registry that holds references to all custom JobDefs and categorizes them.
    /// This provides a safe, efficient, and centralized way to access and query JobDefs
    /// throughout the mod, improving maintainability and performance.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class JobDefRegistry
    {
        public static readonly JobDef LoadTransporters;
        public static readonly JobDef LoadPortal;
        public static readonly JobDef UnloadCarriers;

        /// <summary>
        /// A high-performance set containing all JobDefs related to "loading" operations.
        /// </summary>
        public static readonly HashSet<JobDef> LoadingJobs;

        /// <summary>
        /// A high-performance set containing all JobDefs related to "unloading" operations.
        /// </summary>
        public static readonly HashSet<JobDef> UnloadingJobs;

        // NOTE: 这是一个从ThingDef动态映射到对应JobDef的字典。
        private static readonly Dictionary<ThingDef, JobDef> JobDefMap;


        static JobDefRegistry()
        {
            // 在游戏启动时，从数据库中获取一次我们的JobDef实例并缓存起来。
            LoadTransporters = DefDatabase<JobDef>.GetNamed("LoadTransportersInBulk_Load");
            LoadPortal = DefDatabase<JobDef>.GetNamed("LoadPortalInBulk");
            UnloadCarriers = DefDatabase<JobDef>.GetNamed("UnloadPackAnimalInBulk");

            // 将JobDef实例放入对应的HashSet中，以便进行快速的类型检查。
            LoadingJobs = new HashSet<JobDef>
            {
                LoadTransporters,
                LoadPortal
            };

            UnloadingJobs = new HashSet<JobDef>
            {
                UnloadCarriers
            };

            JobDefMap = new Dictionary<ThingDef, JobDef>();

            // HACK: 我们依赖原版的ThingRequestGroup来动态识别所有类型的运输仓和传送门。
            // 这是为了自动兼容其他Mod添加的、遵循了原版规范的新建筑。
            var transporterGroup = ThingRequestGroup.Transporter;
            var portalGroup = ThingRequestGroup.MapPortal;

            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (transporterGroup.Includes(def))
                {
                    // 将所有被游戏认为是“运输仓”的ThingDef，都映射到处理运输仓的JobDef上。
                    JobDefMap[def] = LoadTransporters;
                    continue; 
                }

                if (portalGroup.Includes(def))
                {
                    // 将所有被游戏认为是“传送门”的ThingDef，都映射到处理传送门的JobDef上。
                    JobDefMap[def] = LoadPortal;
                }
            }
        }

        /// <summary>
        /// Dynamically retrieves the appropriate bulk loading JobDef for a given target ThingDef.
        /// </summary>
        /// <param name="targetDef">The ThingDef of the loading destination (e.g., a transport pod or a pit gate).</param>
        /// <returns>The corresponding JobDef, or null if no match is found.</returns>
        public static JobDef GetJobDefFor(ThingDef targetDef)
        {
            if (targetDef != null && JobDefMap.TryGetValue(targetDef, out JobDef jobDef))
            {
                return jobDef;
            }

            // NOTE: 如果这里报错，通常意味着一个Mod添加了新的装载目标，但没有将其正确地放入
            Log.Error($"[BulkLoad] JobDefRegistry: Could not find a matching JobDef for target ThingDef '{targetDef?.defName}'.");
            return null;
        }

        /// <summary>
        /// Checks if a given JobDef is categorized as a "loading" job.
        /// </summary>
        public static bool IsLoadingJob(JobDef def)
        {
            return def != null && LoadingJobs.Contains(def);
        }

        /// <summary>
        /// Checks if a given JobDef is categorized as an "unloading" job.
        /// </summary>
        public static bool IsUnloadingJob(JobDef def)
        {
            return def != null && UnloadingJobs.Contains(def);
        }
    }
}