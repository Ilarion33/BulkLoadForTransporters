// Copyright (c) 2025 Ilarion. All rights reserved.
//
// BulkLoadForTransporters_VehiclesCompat/CompatMain.cs
using BulkLoadForTransporters.Core; // 引用自主项目
using BulkLoadForTransporters.Core.Utils;
using HarmonyLib;
using RimWorld; // for JobDef
using System.Collections.Generic; // for Dictionary
using System.Linq;
using Vehicles;
using Verse;

namespace BulkLoadForTransporters_VehiclesCompat
{
    [StaticConstructorOnStartup]
    public static class CompatMain
    {
        // 这个 JobDef 将从 XML 中加载
        public static readonly JobDef LoadVehicleInBulk = DefDatabase<JobDef>.GetNamed("LoadVehicleInBulk");

        static CompatMain()
        {
            var harmony = new Harmony("Ilarion.BulkLoadForTransporters.VehiclesCompat");
            harmony.PatchAll(); // 在这个DLL里，PatchAll是安全的

            // 订阅主项目的注册事件
            JobDefRegistry.OnRegisterDefs += RegisterVehicleDefs;

            RegisterDefsPostLoad();

            // 新增：向主模块的“插槽”中注册我们的功能
            RegisterOpportunisticScanners();

            JobDef loadVehicleInBulk = DefDatabase<JobDef>.GetNamed("LoadVehicleInBulk");
            if (loadVehicleInBulk != null)
            {
                JobDefRegistry.LoadingJobs.Add(loadVehicleInBulk);
            }
        }

        private static void RegisterOpportunisticScanners()
        {
            JobDef loadVehicleInBulk = DefDatabase<JobDef>.GetNamed("LoadVehicleInBulk");

            // 注册我们的载具扫描器
            BulkLoad_Utility.OpportunisticTargetScanners.Add(
                pawn => pawn.Map.mapPawns.AllPawns.OfType<VehiclePawn>()
            );

            // 注册我们的 VehicleAdapter 工厂
            BulkLoad_Utility.AdapterFactories.Add(
                thing => thing is VehiclePawn vehicle ? new VehicleAdapter(vehicle) : null
            );
        }

        // 负责在所有 Defs 加载后执行注册
        private static void RegisterDefsPostLoad()
        {
            // 直接访问并修改 JobDefRegistry 的静态字典
            RegisterVehicleDefs(JobDefRegistry.JobDefMap);
        }

        // 这是我们订阅的注册方法
        public static void RegisterVehicleDefs(Dictionary<ThingDef, JobDef> map)
        {
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def is VehicleDef || (def.thingClass != null && typeof(VehicleDef).IsAssignableFrom(def.thingClass)))
                {
                    map[def] = LoadVehicleInBulk;
                }
            }
        }
    }
}