// Copyright (c) 2025 Ilarion. All rights reserved.
//
// HarmonyPatches/LoadTransporters/SmartSubtraction_Patch.cs
using BulkLoadForTransporters.Core;
using BulkLoadForTransporters.Core.Utils;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace BulkLoadForTransporters.HarmonyPatches.LoadTransporters
{
    /// <summary>
    /// Intercepts the CompTransporter's accounting logic to provide a more robust
    /// and precise way of subtracting loaded items from the to-load list.
    /// </summary>
    [HarmonyPatch(typeof(CompTransporter), "SubtractFromToLoadList")]
    public static class SmartSubtraction_Patch
    {
        // NOTE: 通过反射来直接修改Transferable的私有字段，
        private static FieldInfo _countToTransferField = AccessTools.Field(typeof(TransferableOneWay), "countToTransfer");
        private static FieldInfo _editBufferField = AccessTools.Field(typeof(Transferable), "editBuffer");

        public static bool Prepare()
        {
            return LoadedModManager.GetMod<Core.BulkLoadForTransportersMod>().GetSettings<Core.Settings>().enableBulkLoadTransporters;
        }

        /// <summary>
        /// A prefix that completely takes over the item subtraction logic, but only
        /// when triggered by our own custom unload Toils.
        /// </summary>
        public static bool Prefix(CompTransporter __instance, Thing t, int count)
        {
            // 这是一个信号旗检查。只有我们自己的卸货Toil会将此标志设为true。
            if (!Global_Utility.IsExecutingManagedUnload)
            {
                return true; 
            }

            var leftToLoad = __instance.leftToLoad;
            if (leftToLoad == null || count <= 0)
            {
                return true; 
            }

            // 使用我们自己实现的、更可靠的匹配算法来找到正确的记账条目。
            var bestMatch = Global_Utility.FindBestMatchFor(t, leftToLoad);

            if (bestMatch == null)
            {
                // 找不到任何匹配，阻止原版方法
                return false;
            }

            // 确保我们扣减的数量不会超过实际需要的数量。
            int amountToSubtract = Mathf.Min(count, bestMatch.CountToTransfer);
            if (amountToSubtract <= 0)
            {
                return false; 
            }

            // HACK: 直接通过反射修改内部计数值，这是确保记账绝对精确的核心。
            int newCount = bestMatch.CountToTransfer - amountToSubtract;
            _countToTransferField.SetValue(bestMatch, newCount);
            _editBufferField.SetValue(bestMatch, newCount.ToStringCached());

            // 如果某个条目的需求被完全满足，就从待办清单中移除它。
            if (newCount <= 0)
            {
                leftToLoad.Remove(bestMatch);
            }

            // 复刻原版的完成消息逻辑
            if (!__instance.AnyInGroupHasAnythingLeftToLoad)
            {
                Messages.Message("MessageFinishedLoadingTransporters".Translate(), __instance.parent, MessageTypeDefOf.TaskCompletion, true);
            }

            return false;
        }
    }
}