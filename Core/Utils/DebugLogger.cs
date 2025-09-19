// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/Utils/DebugLogger.cs
using System;
using Verse;

namespace BulkLoadForTransporters.Core.Utils
{
    public enum LogCategory
    {
        Planner,
        WorkGiver,
        Toils,
        Manager
    }

    public static class DebugLogger
    {
        public static void LogMessage(LogCategory category, Func<string> messageGenerator)
        {
            if (!Prefs.DevMode) return;
            var settings = LoadedModManager.GetMod<BulkLoadForTransportersMod>().GetSettings<Settings>();

            bool shouldLog = false;
            switch (category)
            {
                case LogCategory.Planner:
                    shouldLog = settings.logPlanner;
                    break;
                case LogCategory.WorkGiver:
                    shouldLog = settings.logWorkGiver;
                    break;
                case LogCategory.Toils:
                    shouldLog = settings.logToils;
                    break;
                case LogCategory.Manager:
                    shouldLog = settings.logManager;
                    break;
            }

            if (shouldLog)
            {
                string message = messageGenerator(); // 字符串在这里被创建
                Log.Message($"[BLFT Debug - {GenTicks.TicksGame}] [{category}] {messageGenerator()}");
            }
        }
    }
}