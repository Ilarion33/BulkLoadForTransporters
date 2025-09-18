// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/BulkLoadForTransportersMod.cs
using UnityEngine;
using Verse;

namespace BulkLoadForTransporters.Core
{
    /// <summary>
    /// The main Mod class. Handles loading settings and drawing the settings window.
    /// </summary>
    public class BulkLoadForTransportersMod : Mod
    {
        private readonly Settings settings;

        private Vector2 scrollPosition = Vector2.zero;

        public BulkLoadForTransportersMod(ModContentPack content) : base(content)
        {
            this.settings = GetSettings<Settings>();
        }

        /// <summary>
        /// Draws all UI elements in the mod settings window.
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {

            Rect mainRect = inRect;
            mainRect.yMax -= 40f;

            // 定义一个逻辑上的“画布”，其高度需要足够大以容纳所有内容，从而激活滚动条。
            Rect contentRect = new Rect(0f, 0f, mainRect.width - 8f, 700f); 

            Listing_Standard listingStandard = new Listing_Standard();

            Widgets.BeginScrollView(mainRect, ref this.scrollPosition, contentRect, false);

            listingStandard.Begin(contentRect);

            GameFont originalFont = Text.Font;
            listingStandard.Gap(8f);

            // --- 通用设置模块 ---
            Text.Font = GameFont.Medium;
            listingStandard.Label("BulkLoadForTransporters.Settings.Header.General".Translate());
            Text.Font = originalFont;

            listingStandard.Gap(8f);

            // Cleanup On Save Setting
            listingStandard.CheckboxLabeled("BulkLoadForTransporters.Settings.CleanupOnSave".Translate(),
                                            ref settings.cleanupOnSave,
                                            "BulkLoadForTransporters.Settings.CleanupOnSave.Tooltip".Translate());

            listingStandard.Gap(8f);

            // AI Update Frequency Setting
            listingStandard.Label("BulkLoadForTransporters.Settings.AiUpdateFrequency".Translate(settings.AiUpdateFrequency), -1f,
                                  "BulkLoadForTransporters.Settings.AiUpdateFrequency.Tooltip".Translate());
            settings.AiUpdateFrequency = (int)listingStandard.Slider(settings.AiUpdateFrequency, 10, 240);

            listingStandard.Gap(8f);


            // Visual Unload Delay Setting
            listingStandard.Label("BulkLoadForTransporters.Settings.VisualUnloadDelay".Translate(settings.visualUnloadDelay), -1f,
                                  "BulkLoadForTransporters.Settings.VisualUnloadDelay.Tooltip".Translate());
            settings.visualUnloadDelay = (int)listingStandard.Slider(settings.visualUnloadDelay, 0, 30);

            listingStandard.Gap(24f);


            // --- 批量装载模块 ---
            DrawFeatureHeader(listingStandard,
                "BulkLoadForTransporters.Settings.Header.BulkLoad".Translate(),
                ref settings.enableBulkLoadTransporters,
                "BulkLoadForTransporters.Settings.Header.BulkLoad.Tooltip".Translate());

            if (settings.enableBulkLoadTransporters)
            {
                listingStandard.Gap(8f);


                listingStandard.CheckboxLabeled("BulkLoadForTransporters.Settings.EnableContinuousLoading".Translate(),
                                                ref settings.enableContinuousLoading,
                                                "BulkLoadForTransporters.Settings.EnableContinuousLoading.Tooltip".Translate());

                listingStandard.Gap(8f);


                listingStandard.CheckboxLabeled("BulkLoadForTransporters.Settings.AutoOpenTransporterContents".Translate(),
                                            ref settings.autoOpenTransporterContents,
                                            "BulkLoadForTransporters.Settings.AutoOpenTransporterContents.Tooltip".Translate());
            }

            listingStandard.Gap(8f); 

            
            DrawFeatureHeader(listingStandard,
                "BulkLoadForTransporters.Settings.Header.BulkLoadPortal".Translate(),
                ref settings.enableBulkLoadPortal,
                "BulkLoadForTransporters.Settings.Header.BulkLoadPortal.Tooltip".Translate());

            if (settings.enableBulkLoadTransporters || settings.enableBulkLoadPortal)
            {                        

                listingStandard.Gap(8f);

               
                // Opportunity Scan Radius Setting
                string radiusValue = (settings.opportunityScanRadius >= 150f)
                    ? "BulkLoadForTransporters.Settings.OpportunityScanRadius.Unlimited".Translate().ToString()
                    : settings.opportunityScanRadius.ToString("F0");
                listingStandard.Label("BulkLoadForTransporters.Settings.OpportunityScanRadius".Translate(radiusValue), -1f,
                                      "BulkLoadForTransporters.Settings.OpportunityScanRadius.Tooltip".Translate());
                settings.opportunityScanRadius = listingStandard.Slider(settings.opportunityScanRadius, 10f, 150f);

                listingStandard.Gap(8f);


                string candidatesLabel = "BulkLoadForTransporters.Settings.PathfindingCandidates".Translate(settings.pathfindingHeuristicCandidates);
                string candidatesTooltip = "BulkLoadForTransporters.Settings.PathfindingCandidates.Tooltip".Translate();
                listingStandard.Label(candidatesLabel, -1f, new TipSignal(candidatesTooltip));
                settings.pathfindingHeuristicCandidates = (int)listingStandard.Slider(settings.pathfindingHeuristicCandidates, 1, 6);

                
            }

            
            listingStandard.Gap(24f);

            // --- 批量卸载驮兽模块 ---
            DrawFeatureHeader(listingStandard,
                "BulkLoadForTransporters.Settings.Header.BulkUnload".Translate(),
                ref settings.enableBulkUnloadCarriers,
                "BulkLoadForTransporters.Settings.Header.BulkUnload.Tooltip".Translate());

            if (settings.enableBulkUnloadCarriers)
            {
                listingStandard.Gap(8f);

                string reserveLabel = "BulkLoadForTransporters.Settings.ReserveCarrierOnUnload".Translate();
                string reserveTooltip = "BulkLoadForTransporters.Settings.ReserveCarrierOnUnload.Tooltip".Translate();
                listingStandard.CheckboxLabeled(reserveLabel, ref settings.reserveCarrierOnUnload, reserveTooltip);

                listingStandard.Gap(8f);


                listingStandard.CheckboxLabeled("BulkLoadForTransporters.Settings.AutoOpenCarrierGear".Translate(),
                                            ref settings.autoOpenCarrierGear,
                                            "BulkLoadForTransporters.Settings.AutoOpenCarrierGear.Tooltip".Translate());

                listingStandard.Gap(8f);

                // Min Free Space to Unload Carrier Setting
                listingStandard.Label("BulkLoadForTransporters.Settings.MinFreeSpaceToUnloadCarrierPct".Translate(settings.minFreeSpaceToUnloadCarrierPct.ToStringPercent()), -1f,
                                      "BulkLoadForTransporters.Settings.MinFreeSpaceToUnloadCarrierPct.Tooltip".Translate());
                settings.minFreeSpaceToUnloadCarrierPct = listingStandard.Slider(settings.minFreeSpaceToUnloadCarrierPct, 0.1f, 0.9f);
                                
            }


            listingStandard.End();

            Widgets.EndScrollView();

            base.DoSettingsWindowContents(inRect);
        }


        public override string SettingsCategory()
        {
            return "Bulk Load For Transporters";
        }

        /// <summary>
        /// A private helper to draw a feature header with an integrated checkbox and dynamic font size, reducing code duplication.
        /// </summary>
        private void DrawFeatureHeader(Listing_Standard listing, TaggedString label, ref bool isEnabled, TaggedString tooltip)
        {
            GameFont originalFont = Text.Font;

            Text.Font = isEnabled ? GameFont.Medium : GameFont.Small;
            listing.CheckboxLabeled(label, ref isEnabled, tooltip);
            Text.Font = originalFont;
        }
    }
}