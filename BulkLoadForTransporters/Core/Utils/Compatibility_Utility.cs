// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Utils/Compatibility_Utility.cs
using HarmonyLib;
using System;
using Verse;

namespace BulkLoadForTransporters.Core.Utils
{
    [StaticConstructorOnStartup]
    public static class Compatibility_Utility
    {
        public static bool IsReplaceStuffLoaded { get; private set; }
        public static readonly Type ReplaceFrameType;


        static Compatibility_Utility()
        {
            ReplaceFrameType = AccessTools.TypeByName("Replace_Stuff.ReplaceFrame");
            IsReplaceStuffLoaded = ReplaceFrameType != null &&
                             ModLister.GetActiveModWithIdentifier("Memegoddess.ReplaceStuff") != null;
        }

        public static bool IsReplaceStuffFrame(Thing t)
        {
            if (!IsReplaceStuffLoaded) return false;
            return ReplaceFrameType.IsInstanceOfType(t);
        }

        public static bool IsReplaceStuffFrameDef(ThingDef def)
        {
            if (!IsReplaceStuffLoaded) return false;

            return def.thingClass == ReplaceFrameType;
        }

        public static bool IsMineableRock_Replica(ThingDef td)
        {
            if (td == null) return false;
            return td.mineable;
        }
    }
}