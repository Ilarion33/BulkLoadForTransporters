// Copyright (c) 2025 Ilarion. All rights reserved.
//
// Core/BulkLoadForTransportersMod.cs
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace BulkLoadForTransporters.Core
{
    /// <summary>
    /// The main Mod class. Handles loading settings and drawing the settings window.
    /// </summary>
    public class BulkLoadForTransportersMod : Mod
    {
        private static bool? _isVehicleFrameworkLoaded;
        public static bool IsVehicleFrameworkLoaded
        {
            get
            {
                if (_isVehicleFrameworkLoaded == null)
                {
                    _isVehicleFrameworkLoaded = ModsConfig.IsActive("SmashPhil.VehicleFramework");
                }
                return _isVehicleFrameworkLoaded.Value;
            }
        }

        private readonly Settings settings;

        private Vector2 _scrollPosition;

        private float _lastCalculatedHeight = 0f;

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
            Rect viewRect = new Rect(0f, 0f, inRect.width - 28f, _lastCalculatedHeight > 0 ? _lastCalculatedHeight : 9999f);

            Listing_Standard listingStandard = new Listing_Standard();

            Widgets.BeginScrollView(inRect, ref _scrollPosition, viewRect);

            listingStandard.Begin(new Rect(0, 0, inRect.width - 28f, 9999f));

            GameFont originalFont = Text.Font;
            listingStandard.Gap(8f);
            


            // --- 通用设置模块 ---
            Color originalColor_A = GUI.color;
            try
            {
                GUI.color = Color.yellow;

                Text.Font = GameFont.Medium;
                listingStandard.Label("BulkLoadForTransporters.Settings.Header.General".Translate());
                Text.Font = originalFont;
            }
            finally
            {
                GUI.color = originalColor_A;
            }

            listingStandard.Gap(8f);

            // Cleanup On Save Setting
            listingStandard.CheckboxLabeled("BulkLoadForTransporters.Settings.CleanupOnSave".Translate(),
                                            ref settings.cleanupOnSave,
                                            "BulkLoadForTransporters.Settings.CleanupOnSave.Tooltip".Translate());

            listingStandard.Gap(8f);

            listingStandard.CheckboxLabeled("BulkLoadForTransporters.Settings.EnableSoftlockCleaner".Translate(),
                                            ref settings.enableSoftlockCleaner,
                                            "BulkLoadForTransporters.Settings.EnableSoftlockCleaner.Tooltip".Translate());

            listingStandard.Gap(8f);

            listingStandard.CheckboxLabeled("BulkLoadForTransporters.Settings.CheatIgnoreInventoryMass".Translate(),
                                            ref settings.cheatIgnoreInventoryMass,
                                            "BulkLoadForTransporters.Settings.CheatIgnoreInventoryMass.Tooltip".Translate());

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



            listingStandard.Gap(8f);

            // 调试设置模块
            Color originalColor_B = GUI.color;
            try
            {
                GUI.color = Prefs.DevMode ? Color.yellow : Color.gray;
                Text.Font = Prefs.DevMode ? GameFont.Medium : GameFont.Small;
                listingStandard.Label("BulkLoadForTransporters.Settings.Header.Debug".Translate(), -1f,
                                      new TipSignal("BulkLoadForTransporters.Settings.Header.Debug.Tooltip".Translate()));
                Text.Font = originalFont;
            }
            finally
            {
                GUI.color = originalColor_B;
            }

            // 只有在开发者模式下才显示详细选项
            if (Prefs.DevMode)
            {
                listingStandard.Gap(8f);

                listingStandard.CheckboxLabeled("BulkLoadForTransporters.Settings.Debug.LogWorkGiver".Translate(),
                                    ref settings.logWorkGiver,
                                    "BulkLoadForTransporters.Settings.Debug.LogWorkGiver.Tooltip".Translate());
                listingStandard.Gap(8f);

                listingStandard.CheckboxLabeled("BulkLoadForTransporters.Settings.Debug.LogPlanner".Translate(),
                                                ref settings.logPlanner,
                                                "BulkLoadForTransporters.Settings.Debug.LogPlanner.Tooltip".Translate());
                listingStandard.Gap(8f);

                listingStandard.CheckboxLabeled("BulkLoadForTransporters.Settings.Debug.LogToils".Translate(),
                                                ref settings.logToils,
                                                "BulkLoadForTransporters.Settings.Debug.LogToils.Tooltip".Translate());
                listingStandard.Gap(8f);

                listingStandard.CheckboxLabeled("BulkLoadForTransporters.Settings.Debug.LogManager".Translate(),
                                                ref settings.logManager,
                                                "BulkLoadForTransporters.Settings.Debug.LogManager.Tooltip".Translate());
            }



            listingStandard.Gap(28f);


            // --- 批量搬砖模块 ---
            DrawFeatureHeader(listingStandard,
                "BulkLoadForTransporters.Settings.Header.BulkConstructionDelivery".Translate(),
                ref settings.enableBulkConstructionDelivery,
                "BulkLoadForTransporters.Settings.Header.BulkConstructionDelivery.Tooltip".Translate());

            if (settings.enableBulkConstructionDelivery)
            {
                listingStandard.Gap(8f);
                                
                // --- 地块分组大小设置 ---
                listingStandard.Label(
                    "BulkLoadForTransporters.Settings.ConstructionGroupingGridSize".Translate(settings.constructionGroupingGridSize),
                    -1f,
                    "BulkLoadForTransporters.Settings.ConstructionGroupingGridSize.Tooltip".Translate());
                settings.constructionGroupingGridSize = (int)listingStandard.Slider(settings.constructionGroupingGridSize, 16, 128);
                listingStandard.Gap(8f);

                // --- 接力扫描半径设置 ---
                string chainRadiusValue = (settings.constructionChainScanRadius <= 1f)
                    ? "BulkLoadForTransporters.Settings.ConstructionChainScanRadius.Disabled".Translate().ToString()
                    : settings.constructionChainScanRadius.ToString("F0");
                listingStandard.Label(
                    "BulkLoadForTransporters.Settings.ConstructionChainScanRadius".Translate(chainRadiusValue),
                    -1f,
                    "BulkLoadForTransporters.Settings.ConstructionChainScanRadius.Tooltip".Translate());
                settings.constructionChainScanRadius = (int)listingStandard.Slider(settings.constructionChainScanRadius, 1f, 60f);

            }



            listingStandard.Gap(8f);

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

            

            if (settings.enableBulkLoadTransporters || settings.enableBulkLoadPortal || settings.enableBulkConstructionDelivery)
            {                        

                listingStandard.Gap(8f);

               
                // Opportunity Scan Radius Setting
                string radiusValue = (settings.opportunityScanRadius >= 150f)
                    ? "BulkLoadForTransporters.Settings.OpportunityScanRadius.Unlimited".Translate().ToString()
                    : settings.opportunityScanRadius.ToString("F0");
                listingStandard.Label("BulkLoadForTransporters.Settings.OpportunityScanRadius".Translate(radiusValue), -1f,
                                      "BulkLoadForTransporters.Settings.OpportunityScanRadius.Tooltip".Translate());
                settings.opportunityScanRadius = listingStandard.Slider(settings.opportunityScanRadius, 30f, 150f);

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
            listingStandard.Gap(8f);

            float buttonWidth = 130f;
            float buttonHeight = 30f;
            Rect buttonRect = listingStandard.GetRect(buttonHeight);
            buttonRect.x = (listingStandard.ColumnWidth - buttonWidth) / 2;
            buttonRect.width = buttonWidth;

            if (listingStandard.ButtonText("BulkLoadForTransporters.Settings.ResetButton".Translate()))
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    "BulkLoadForTransporters.Settings.ResetConfirmDialog".Translate(),
                    "Confirm".Translate(),
                    () => {
                        settings.Reset();
                        SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                    },
                    "Cancel".Translate(),null,null,true,null,null));
            }
            listingStandard.Gap(48f);


            _lastCalculatedHeight = listingStandard.CurHeight;

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
            Color originalColor = GUI.color;

            try
            {
                if (isEnabled)
                {
                    Text.Font = GameFont.Medium;
                    GUI.color = Color.yellow;
                }
                else
                {
                    Text.Font = GameFont.Small;
                    GUI.color = Color.gray;
                }

                listing.CheckboxLabeled(label, ref isEnabled, tooltip);
            }
            finally
            {
                Text.Font = originalFont;
                GUI.color = originalColor;
            }
        }
    }
}