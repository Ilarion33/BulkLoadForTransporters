// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Utils/Compatibility_Utility.cs
using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace BulkLoadForTransporters.Core.Utils
{
    [StaticConstructorOnStartup]
    public static class Compatibility_Utility
    {
        public static bool IsReplaceStuffLoaded { get; private set; }
        public static readonly Type ReplaceFrameType;
        private const string NanameWallSuffix = "_NAWDiagonal";

        static Compatibility_Utility()
        {
            ReplaceFrameType = AccessTools.TypeByName("Replace_Stuff.ReplaceFrame");
            IsReplaceStuffLoaded = ReplaceFrameType != null &&
                             ModLister.GetActiveModWithIdentifier("Memegoddess.ReplaceStuff") != null;
        }

        /// <summary>
        /// 权威的检查方法，用于判断一个 ThingDef 是否属于我们不应处理的特殊建设类型。
        /// </summary>
        public static bool IsIncompatibleConstructionDef(ThingDef def)
        {
            if (def == null) return true;

            // 黑名单 #1: Replace Stuff
            if (IsReplaceStuffLoaded && def.thingClass == ReplaceFrameType)
            {
                return true;
            }

            // 黑名单 #2: Naname Walls
            if (def.defName.EndsWith(NanameWallSuffix))
            {
                return true;
            }

            // 黑名单 #3: Blueprint_Install
            if (typeof(Blueprint_Install).IsAssignableFrom(def.thingClass))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 权威的检查方法，用于判断一个 Thing 实例是否属于我们不应处理的特殊建设类型。
        /// </summary>
        public static bool IsIncompatibleConstructionThing(Thing t)
        {
            if (t == null) return true;
            return IsIncompatibleConstructionDef(t.def);
        }

        //public static bool IsReplaceStuffFrame(Thing t)
        //{
        //    if (!IsReplaceStuffLoaded) return false;
        //    return ReplaceFrameType.IsInstanceOfType(t);
        //}

        //public static bool IsReplaceStuffFrameDef(ThingDef def)
        //{
        //    if (!IsReplaceStuffLoaded) return false;

        //    return def.thingClass == ReplaceFrameType;
        //}

        public static bool IsMineableRock_Replica(ThingDef td)
        {
            if (td == null) return false;
            return td.mineable;
        }
    }
}