// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Main.cs
using HarmonyLib;
using Verse;

[StaticConstructorOnStartup]
public static class Main
{
    static Main()
    {
        var harmony = new Harmony("Ilarion.BulkLoadForTransporters");
        harmony.PatchAll(); 
    }
}