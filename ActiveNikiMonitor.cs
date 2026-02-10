#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// ActiveNikiMonitor - Replicates ActiveNikiTrader signal detection logic as an Indicator
    /// Does not place trades, only monitors and logs signals
    /// Stays active during discretionary trading (unlike strategies which get disabled)
    /// </summary>
    public class ActiveNikiMonitor : Indicator
    {
        #region Fields
        // ninZa indicator references (for VPS with licensed indicators)
        private object rubyRiver, vidyaPro, easyTrend, dragonTrend, solarWave, ninZaT3Pro, aaaTrendSync;
        private FieldInfo rrIsUptrend, vyIsUptrend, etIsUptrend, dtPrevSignal, swIsUptrend, swCountWave, t3pIsUptrend, aaaIsUptrend;
        
        // Native AIQ_1 indicator reference (from AIQ folder)
        private object nativeAiq1;
        private FieldInfo nativeAiq1TrendState;
        private bool useNativeAiq1;
        
        // Native AIQ_SuperBands indicator reference
        private object nativeAiqSuperBands;
        private FieldInfo nativeAiqSBIsUptrend;
        private bool useNativeAiqSB;
        
        // Chart-attached equivalent indicator references
        private object chartAiq1Equivalent, chartRubyRiverEquiv, chartDragonTrendEquiv;
        private object chartVidyaProEquiv, chartEasyTrendEquiv, chartSolarWaveEquiv, chartT3ProEquiv;
        private object chartAAATrendSyncEquiv;
        private object chartAiqSuperBandsEquiv;
        private PropertyInfo aiq1IsUptrend;
        private PropertyInfo rrEquivIsUptrend, dtEquivPrevSignal, vyEquivIsUptrend, etEquivIsUptrend;
        private PropertyInfo swEquivIsUptrend, swEquivCountWave, t3pEquivIsUptrend;
        private PropertyInfo aaaEquivIsUptrend;
        private PropertyInfo sbEquivIsUptrend;
        private bool useChartAiq1, useChartRR, useChartDT, useChartVY, useChartET, useChartSW, useChartT3P;
        private bool useChartAAA;
        private bool useChartSB;
        
        // Equivalent indicators (hosted by indicator - fallback only)
        private T3ProEquivalent t3ProEquivalent;
        private VIDYAProEquivalent vidyaProEquivalent;
        private EasyTrendEquivalent easyTrendEquivalent;
        private RubyRiverEquivalent rubyRiverEquivalent;
        private DragonTrendEquivalent dragonTrendEquivalent;
        private SolarWaveEquivalent solarWaveEquivalent;
        private AIQ_1Equivalent aiq1Equivalent;
        
        // Auto-switch flags
        private bool useHostedT3Pro, useHostedVIDYAPro, useHostedEasyTrend;
        private bool useHostedRubyRiver, useHostedDragonTrend, useHostedSolarWave;
        private bool indicatorsReady;
        
        // Trigger tracking
        private int barsSinceYellowSquare = -1;
        private int barsSinceOrangeSquare = -1;
        private int barsSinceLastSignal = -1;
        private DateTime lastSignalTime = DateTime.MinValue;
        private bool prevAIQ1_IsUp;
        private bool isFirstBar = true;
        
        // Previous state tracking for confirmation detection
        private bool prevRR_IsUp, prevDT_IsUp, prevVY_IsUp, prevET_IsUp, prevSW_IsUp, prevT3P_IsUp, prevAAA_IsUp, prevSB_IsUp;
        
        // Panel UI elements
        private Grid controlPanel;
        private bool panelActive;
        private bool isDragging;
        private bool isResizing;
        private Point dragStartPoint;
        private double resizeStartWidth, resizeStartHeight;
        private TranslateTransform panelTransform;
        private string panelSettingsFile;
        private CheckBox chkRubyRiver, chkDragonTrend, chkSolarWave, chkVIDYA, chkEasyTrend, chkT3Pro, chkAAASync, chkSuperBands;
        private TextBlock lblRubyRiver, lblDragonTrend, lblSolarWave, lblVIDYA, lblEasyTrend, lblT3Pro, lblAAASync, lblSuperBands;
        private TextBlock lblAIQ1Status, lblWindowStatus;
        private TextBlock lblAIQ1Name;  // Dynamic trigger label
        private TextBlock lblTradeStatus, lblSessionStats, lblTriggerMode, lblLastSignal, lblSubtitle;
        private Border signalBorder;
        
        // Resize edge handling (matching ActiveNikiTrader.Panel.cs)
        private enum ResizeEdge { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }
        private ResizeEdge currentResizeEdge = ResizeEdge.None;
        private const double EdgeThreshold = 8;
        private double panelWidth = 200;
        private double panelHeight = 400;
        private double minPanelWidth = 150;
        private double minPanelHeight = 200;
        private Point resizeStartMousePos;
        private double resizeStartLeft, resizeStartTop;
        
        // Session tracking
        private int signalCount;
        private string lastSignalText = "";
        private string logFilePath;
        private StreamWriter logWriter;
        private string chartSessionId;
        
        // CSV debug logging
        private string csvLogFilePath;
        private StreamWriter csvWriter;
        #endregion
        
        #region Parameters
        [NinjaScriptProperty]
        [Range(2, 8)]
        [Display(Name="Min Confluence Required", Description="Minimum indicators agreeing (2-8)", Order=1, GroupName="1. Signal Filters")]
        public int MinConfluenceRequired { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 3)]
        [Display(Name="Max Bars After Yellow Square", Description="Bars after AIQ1 flip to confirm (0-3)", Order=2, GroupName="1. Signal Filters")]
        public int MaxBarsAfterYellowSquare { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name="Min Solar Wave Count", Order=3, GroupName="1. Signal Filters")]
        public int MinSolarWaveCount { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Cooldown Bars", Description="Minimum bars between signals (0=disabled)", Order=4, GroupName="1. Signal Filters")]
        public int CooldownBars { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Use Time-Based Cooldown", Description="Use seconds instead of bars", Order=5, GroupName="1. Signal Filters")]
        public bool UseTimeBasedCooldown { get; set; }
        
        [NinjaScriptProperty]
        [Range(10, 600)]
        [Display(Name="Cooldown Seconds", Description="Seconds between signals", Order=6, GroupName="1. Signal Filters")]
        public int CooldownSeconds { get; set; }
        
        [NinjaScriptProperty][Display(Name="Use Ruby River", Order=1, GroupName="2. Indicator Selection")]
        public bool UseRubyRiver { get; set; }
        [NinjaScriptProperty][Display(Name="Use Dragon Trend", Order=2, GroupName="2. Indicator Selection")]
        public bool UseDragonTrend { get; set; }
        [NinjaScriptProperty][Display(Name="Use Solar Wave", Order=3, GroupName="2. Indicator Selection")]
        public bool UseSolarWave { get; set; }
        [NinjaScriptProperty][Display(Name="Use VIDYA Pro", Order=4, GroupName="2. Indicator Selection")]
        public bool UseVIDYAPro { get; set; }
        [NinjaScriptProperty][Display(Name="Use Easy Trend", Order=5, GroupName="2. Indicator Selection")]
        public bool UseEasyTrend { get; set; }
        [NinjaScriptProperty][Display(Name="Use T3 Pro", Order=6, GroupName="2. Indicator Selection")]
        public bool UseT3Pro { get; set; }
        [NinjaScriptProperty][Display(Name="Use AAA TrendSync", Order=7, GroupName="2. Indicator Selection")]
        public bool UseAAATrendSync { get; set; }
        [NinjaScriptProperty][Display(Name="Use AIQ SuperBands", Order=8, GroupName="2. Indicator Selection")]
        public bool UseAIQSuperBands { get; set; }
        
        [NinjaScriptProperty][Range(1, 100)][Display(Name="T3Pro Period", Order=1, GroupName="3. T3 Pro Settings")]
        public int T3ProPeriod { get; set; }
        [NinjaScriptProperty][Range(1, 10)][Display(Name="T3Pro TCount", Order=2, GroupName="3. T3 Pro Settings")]
        public int T3ProTCount { get; set; }
        [NinjaScriptProperty][Range(0.0, 2.0)][Display(Name="T3Pro VFactor", Order=3, GroupName="3. T3 Pro Settings")]
        public double T3ProVFactor { get; set; }
        [NinjaScriptProperty][Display(Name="T3Pro Chaos Smoothing", Order=4, GroupName="3. T3 Pro Settings")]
        public bool T3ProChaosSmoothingEnabled { get; set; }
        [NinjaScriptProperty][Range(1, 50)][Display(Name="T3Pro Chaos Period", Order=5, GroupName="3. T3 Pro Settings")]
        public int T3ProChaosSmoothingPeriod { get; set; }
        [NinjaScriptProperty][Display(Name="T3Pro Filter Enabled", Order=6, GroupName="3. T3 Pro Settings")]
        public bool T3ProFilterEnabled { get; set; }
        [NinjaScriptProperty][Range(0.1, 10.0)][Display(Name="T3Pro Filter Multiplier", Order=7, GroupName="3. T3 Pro Settings")]
        public double T3ProFilterMultiplier { get; set; }
        
        [NinjaScriptProperty][Range(1, 100)][Display(Name="VIDYA Period", Order=1, GroupName="4. VIDYA Pro Settings")]
        public int VIDYAPeriod { get; set; }
        [NinjaScriptProperty][Range(1, 100)][Display(Name="VIDYA Volatility Period", Order=2, GroupName="4. VIDYA Pro Settings")]
        public int VIDYAVolatilityPeriod { get; set; }
        [NinjaScriptProperty][Display(Name="VIDYA Smoothing Enabled", Order=3, GroupName="4. VIDYA Pro Settings")]
        public bool VIDYASmoothingEnabled { get; set; }
        [NinjaScriptProperty][Range(1, 50)][Display(Name="VIDYA Smoothing Period", Order=4, GroupName="4. VIDYA Pro Settings")]
        public int VIDYASmoothingPeriod { get; set; }
        [NinjaScriptProperty][Display(Name="VIDYA Filter Enabled", Order=5, GroupName="4. VIDYA Pro Settings")]
        public bool VIDYAFilterEnabled { get; set; }
        [NinjaScriptProperty][Range(0.1, 10.0)][Display(Name="VIDYA Filter Multiplier", Order=6, GroupName="4. VIDYA Pro Settings")]
        public double VIDYAFilterMultiplier { get; set; }
        
        [NinjaScriptProperty][Range(1, 100)][Display(Name="EasyTrend Period", Order=1, GroupName="5. Easy Trend Settings")]
        public int EasyTrendPeriod { get; set; }
        [NinjaScriptProperty][Display(Name="EasyTrend Smoothing Enabled", Order=2, GroupName="5. Easy Trend Settings")]
        public bool EasyTrendSmoothingEnabled { get; set; }
        [NinjaScriptProperty][Range(1, 50)][Display(Name="EasyTrend Smoothing Period", Order=3, GroupName="5. Easy Trend Settings")]
        public int EasyTrendSmoothingPeriod { get; set; }
        [NinjaScriptProperty][Display(Name="EasyTrend Filter Enabled", Order=4, GroupName="5. Easy Trend Settings")]
        public bool EasyTrendFilterEnabled { get; set; }
        [NinjaScriptProperty][Range(0.01, 10.0)][Display(Name="EasyTrend Filter Multiplier", Order=5, GroupName="5. Easy Trend Settings")]
        public double EasyTrendFilterMultiplier { get; set; }
        [NinjaScriptProperty][Range(1, 200)][Display(Name="EasyTrend ATR Period", Order=6, GroupName="5. Easy Trend Settings")]
        public int EasyTrendATRPeriod { get; set; }
        
        [NinjaScriptProperty][Range(1, 100)][Display(Name="RubyRiver MA Period", Order=1, GroupName="6. Ruby River Settings")]
        public int RubyRiverMAPeriod { get; set; }
        [NinjaScriptProperty][Display(Name="RubyRiver Smoothing Enabled", Order=2, GroupName="6. Ruby River Settings")]
        public bool RubyRiverSmoothingEnabled { get; set; }
        [NinjaScriptProperty][Range(1, 50)][Display(Name="RubyRiver Smoothing Period", Order=3, GroupName="6. Ruby River Settings")]
        public int RubyRiverSmoothingPeriod { get; set; }
        [NinjaScriptProperty][Range(0.01, 2.0)][Display(Name="RubyRiver Offset Multiplier", Order=4, GroupName="6. Ruby River Settings")]
        public double RubyRiverOffsetMultiplier { get; set; }
        [NinjaScriptProperty][Range(1, 200)][Display(Name="RubyRiver Offset Period", Order=5, GroupName="6. Ruby River Settings")]
        public int RubyRiverOffsetPeriod { get; set; }
        
        [NinjaScriptProperty][Range(1, 100)][Display(Name="DragonTrend Period", Order=1, GroupName="7. Dragon Trend Settings")]
        public int DragonTrendPeriod { get; set; }
        [NinjaScriptProperty][Display(Name="DragonTrend Smoothing Enabled", Order=2, GroupName="7. Dragon Trend Settings")]
        public bool DragonTrendSmoothingEnabled { get; set; }
        [NinjaScriptProperty][Range(1, 50)][Display(Name="DragonTrend Smoothing Period", Order=3, GroupName="7. Dragon Trend Settings")]
        public int DragonTrendSmoothingPeriod { get; set; }
        
        [NinjaScriptProperty][Range(1, 200)][Display(Name="SolarWave ATR Period", Order=1, GroupName="8. Solar Wave Settings")]
        public int SolarWaveATRPeriod { get; set; }
        [NinjaScriptProperty][Range(0.1, 10.0)][Display(Name="SolarWave Trend Multiplier", Order=2, GroupName="8. Solar Wave Settings")]
        public double SolarWaveTrendMultiplier { get; set; }
        [NinjaScriptProperty][Range(0.1, 10.0)][Display(Name="SolarWave Stop Multiplier", Order=3, GroupName="8. Solar Wave Settings")]
        public double SolarWaveStopMultiplier { get; set; }
        
        [NinjaScriptProperty][Display(Name="Enable Sound Alert", Order=1, GroupName="9. Alerts")]
        public bool EnableSoundAlert { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Enable Indicator CSV Log", Description="Log indicator values to CSV", Order=1, GroupName="10. Debug")]
        public bool EnableIndicatorCSVLog { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Log Bar Details", Description="Log detailed bar info", Order=2, GroupName="10. Debug")]
        public bool LogBarDetails { get; set; }
        #endregion
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "ActiveNikiMonitor";
                Description = "Monitors 8-indicator confluence signals - Mirrors ActiveNikiTrader (Signal Only mode)";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = false;
                
                // Signal filters - match ActiveNikiTrader defaults
                MinConfluenceRequired = 6;
                MaxBarsAfterYellowSquare = 3;
                MinSolarWaveCount = 1;
                CooldownBars = 10;
                UseTimeBasedCooldown = true;
                CooldownSeconds = 90;
                
                // Indicator selection - all 8 enabled by default
                UseRubyRiver = true;
                UseDragonTrend = true;
                UseSolarWave = true;
                UseVIDYAPro = true;
                UseEasyTrend = true;
                UseT3Pro = true;
                UseAAATrendSync = true;
                UseAIQSuperBands = true;
                
                // T3 Pro defaults
                T3ProPeriod = 14;
                T3ProTCount = 3;
                T3ProVFactor = 0.7;
                T3ProChaosSmoothingEnabled = true;
                T3ProChaosSmoothingPeriod = 5;
                T3ProFilterEnabled = true;
                T3ProFilterMultiplier = 4.0;
                
                // VIDYA Pro defaults
                VIDYAPeriod = 9;
                VIDYAVolatilityPeriod = 9;
                VIDYASmoothingEnabled = true;
                VIDYASmoothingPeriod = 5;
                VIDYAFilterEnabled = true;
                VIDYAFilterMultiplier = 4.0;
                
                // Easy Trend defaults
                EasyTrendPeriod = 30;
                EasyTrendSmoothingEnabled = true;
                EasyTrendSmoothingPeriod = 7;
                EasyTrendFilterEnabled = true;
                EasyTrendFilterMultiplier = 0.5;
                EasyTrendATRPeriod = 100;
                
                // Ruby River defaults
                RubyRiverMAPeriod = 20;
                RubyRiverSmoothingEnabled = true;
                RubyRiverSmoothingPeriod = 5;
                RubyRiverOffsetMultiplier = 0.15;
                RubyRiverOffsetPeriod = 100;
                
                // Dragon Trend defaults
                DragonTrendPeriod = 10;
                DragonTrendSmoothingEnabled = true;
                DragonTrendSmoothingPeriod = 5;
                
                // Solar Wave defaults
                SolarWaveATRPeriod = 100;
                SolarWaveTrendMultiplier = 2;
                SolarWaveStopMultiplier = 4;
                
                EnableSoundAlert = true;
                
                // Debug - CSV and Bar logging ON by default (per user request)
                EnableIndicatorCSVLog = true;
                LogBarDetails = true;
            }
            else if (State == State.DataLoaded)
            {
                chartSessionId = DateTime.Now.ToString("HHmmss") + "_" + new Random().Next(1000, 9999);
                InitializeLogFile();
                InitializeCSVLog();
                
                // Initialize all equivalent indicators (hosted - fallback only)
                t3ProEquivalent = T3ProEquivalent(T3ProMAType.EMA, T3ProPeriod, T3ProTCount, T3ProVFactor,
                    T3ProChaosSmoothingEnabled, T3ProMAType.DEMA, T3ProChaosSmoothingPeriod,
                    T3ProFilterEnabled, T3ProFilterMultiplier, 14, true, false, "▲", "▼", 10);
                vidyaProEquivalent = VIDYAProEquivalent(VIDYAPeriod, VIDYAVolatilityPeriod, VIDYASmoothingEnabled,
                    VIDYAProMAType.EMA, VIDYASmoothingPeriod, VIDYAFilterEnabled, VIDYAFilterMultiplier, 14,
                    true, false, "▲", "▼", 10);
                easyTrendEquivalent = EasyTrendEquivalent(EasyTrendMAType.EMA, EasyTrendPeriod, EasyTrendSmoothingEnabled,
                    EasyTrendMAType.EMA, EasyTrendSmoothingPeriod, EasyTrendFilterEnabled, true,
                    EasyTrendFilterMultiplier, EasyTrendFilterUnit.ninZaATR, EasyTrendATRPeriod,
                    true, false, "▲ + Easy", "Easy + ▼", 10);
                rubyRiverEquivalent = RubyRiverEquivalent(RubyRiverMAType.EMA, RubyRiverMAPeriod, RubyRiverSmoothingEnabled,
                    RubyRiverMAType.LinReg, RubyRiverSmoothingPeriod, RubyRiverOffsetMultiplier, RubyRiverOffsetPeriod,
                    true, false, "▲", "▼", 10);
                dragonTrendEquivalent = DragonTrendEquivalent(DragonTrendPeriod, DragonTrendSmoothingEnabled,
                    DragonTrendMAType.EMA, DragonTrendSmoothingPeriod, false, "▲", "▼", 10);
                solarWaveEquivalent = SolarWaveEquivalent(SolarWaveATRPeriod, SolarWaveTrendMultiplier, SolarWaveStopMultiplier,
                    2, 1, 5, 10, 10, true, false, "▲ + Trend", "Trend + ▼", 12);
                
                // Initialize AIQ_1 trigger indicator (hosted fallback)
                aiq1Equivalent = AIQ_1Equivalent(3, 0, AIQ1EquivMAMethod.MA1, true, 0.05, 0.05, 0.03, 0.03,
                    true, 15, 100, false, 4, Brushes.Orange, Brushes.Orange);
                
                LogAlways($"ActiveNikiMonitor | 8-indicator confluence | Signal≥{MinConfluenceRequired} | CD={CooldownBars}");
                if (EnableIndicatorCSVLog)
                    LogAlways($"📊 CSV Indicator Log: ENABLED");
                if (LogBarDetails)
                    LogAlways($"📝 Bar Detail Logging: ENABLED");
            }
            else if (State == State.Historical)
            {
                LoadNinZaIndicators();
                LogDetectedIndicators();
                if (ChartControl != null) ChartControl.Dispatcher.InvokeAsync(CreateControlPanel);
            }
            else if (State == State.Terminated)
            {
                if (ChartControl != null) ChartControl.Dispatcher.InvokeAsync(RemoveControlPanel);
                CloseLogFile();
                CloseCSVLog();
            }
        }
        
        #region Indicator Loading
        private void LoadNinZaIndicators()
        {
            if (ChartControl?.Indicators == null) 
            { 
                useHostedT3Pro = useHostedVIDYAPro = useHostedEasyTrend = true;
                useHostedRubyRiver = useHostedDragonTrend = useHostedSolarWave = true;
                useNativeAiq1 = useChartAiq1 = useChartRR = useChartDT = useChartVY = useChartET = useChartSW = useChartT3P = useChartAAA = useChartSB = false;
                useNativeAiqSB = false;
                indicatorsReady = true; 
                return; 
            }
            var flagsPrivate = BindingFlags.NonPublic | BindingFlags.Instance;
            var flagsPublic = BindingFlags.Public | BindingFlags.Instance;
            
            foreach (var ind in ChartControl.Indicators)
            {
                var t = ind.GetType();
                string typeName = t.Name;
                
                switch (typeName)
                {
                    // ninZa licensed indicators
                    case "ninZaRubyRiver": rubyRiver = ind; rrIsUptrend = t.GetField("isUptrend", flagsPrivate); break;
                    case "ninZaVIDYAPro": vidyaPro = ind; vyIsUptrend = t.GetField("isUptrend", flagsPrivate); break;
                    case "ninZaEasyTrend": easyTrend = ind; etIsUptrend = t.GetField("isUptrend", flagsPrivate); break;
                    case "ninZaDragonTrend": dragonTrend = ind; dtPrevSignal = t.GetField("prevSignal", flagsPrivate); break;
                    case "ninZaSolarWave": solarWave = ind; swIsUptrend = t.GetField("isUptrend", flagsPrivate); swCountWave = t.GetField("countWave", flagsPrivate); break;
                    case "ninZaT3Pro": ninZaT3Pro = ind; t3pIsUptrend = t.GetField("isUptrend", flagsPrivate); break;
                    case "ninZaAAATrendSync": aaaTrendSync = ind; aaaIsUptrend = t.GetField("isUptrend", flagsPrivate); break;
                    
                    // Native AIQ_1 indicator
                    case "AIQ_1":
                        nativeAiq1 = ind;
                        nativeAiq1TrendState = t.GetField("trendState", flagsPrivate);
                        break;
                    
                    // Native AIQ_SuperBands indicator
                    case "AIQ_SuperBands":
                        nativeAiqSuperBands = ind;
                        nativeAiqSBIsUptrend = t.GetField("isUptrend", flagsPrivate);
                        if (nativeAiqSBIsUptrend == null)
                            nativeAiqSBIsUptrend = t.GetField("trendState", flagsPrivate);
                        break;
                    
                    // Chart-attached equivalent indicators
                    case "AIQ_1Equivalent": 
                        chartAiq1Equivalent = ind; 
                        aiq1IsUptrend = t.GetProperty("IsUptrend", flagsPublic); 
                        break;
                    case "RubyRiverEquivalent":
                        chartRubyRiverEquiv = ind;
                        rrEquivIsUptrend = t.GetProperty("IsUptrend", flagsPublic);
                        break;
                    case "DragonTrendEquivalent":
                        chartDragonTrendEquiv = ind;
                        dtEquivPrevSignal = t.GetProperty("PrevSignal", flagsPublic);
                        break;
                    case "VIDYAProEquivalent":
                        chartVidyaProEquiv = ind;
                        vyEquivIsUptrend = t.GetProperty("IsUptrend", flagsPublic);
                        break;
                    case "EasyTrendEquivalent":
                        chartEasyTrendEquiv = ind;
                        etEquivIsUptrend = t.GetProperty("IsUptrend", flagsPublic);
                        break;
                    case "SolarWaveEquivalent":
                        chartSolarWaveEquiv = ind;
                        swEquivIsUptrend = t.GetProperty("IsUptrend", flagsPublic);
                        swEquivCountWave = t.GetProperty("CountWave", flagsPublic);
                        break;
                    case "T3ProEquivalent":
                        chartT3ProEquiv = ind;
                        t3pEquivIsUptrend = t.GetProperty("IsUptrend", flagsPublic);
                        break;
                    case "AAATrendSyncEquivalent":
                        chartAAATrendSyncEquiv = ind;
                        aaaEquivIsUptrend = t.GetProperty("IsUptrend", flagsPublic);
                        break;
                    case "AIQ_SuperBandsEquivalent":
                        chartAiqSuperBandsEquiv = ind;
                        sbEquivIsUptrend = t.GetProperty("IsUptrend", flagsPublic);
                        break;
                }
            }
            
            // Determine source priority
            useNativeAiq1 = nativeAiq1 != null && nativeAiq1TrendState != null;
            useChartAiq1 = !useNativeAiq1 && chartAiq1Equivalent != null && aiq1IsUptrend != null;
            
            useChartRR = chartRubyRiverEquiv != null && rrEquivIsUptrend != null;
            useChartDT = chartDragonTrendEquiv != null && dtEquivPrevSignal != null;
            useChartVY = chartVidyaProEquiv != null && vyEquivIsUptrend != null;
            useChartET = chartEasyTrendEquiv != null && etEquivIsUptrend != null;
            useChartSW = chartSolarWaveEquiv != null && swEquivIsUptrend != null;
            useChartT3P = chartT3ProEquiv != null && t3pEquivIsUptrend != null;
            useChartAAA = chartAAATrendSyncEquiv != null && aaaEquivIsUptrend != null;
            
            useNativeAiqSB = nativeAiqSuperBands != null && nativeAiqSBIsUptrend != null;
            useChartSB = !useNativeAiqSB && chartAiqSuperBandsEquiv != null && sbEquivIsUptrend != null;
            
            useHostedT3Pro = ninZaT3Pro == null && !useChartT3P;
            useHostedVIDYAPro = vidyaPro == null && !useChartVY;
            useHostedEasyTrend = easyTrend == null && !useChartET;
            useHostedRubyRiver = rubyRiver == null && !useChartRR;
            useHostedDragonTrend = dragonTrend == null && !useChartDT;
            useHostedSolarWave = solarWave == null && !useChartSW;
            
            indicatorsReady = true;
        }
        
        private void LogDetectedIndicators()
        {
            LogAlways($"--- Indicators Detected on Chart ---");
            LogAlways($"  ninZaRubyRiver:    {(rubyRiver != null ? "FOUND" : "not found")}");
            LogAlways($"  ninZaDragonTrend:  {(dragonTrend != null ? "FOUND" : "not found")}");
            LogAlways($"  ninZaVIDYAPro:     {(vidyaPro != null ? "FOUND" : "not found")}");
            LogAlways($"  ninZaEasyTrend:    {(easyTrend != null ? "FOUND" : "not found")}");
            LogAlways($"  ninZaSolarWave:    {(solarWave != null ? "FOUND" : "not found")}");
            LogAlways($"  ninZaT3Pro:        {(ninZaT3Pro != null ? "FOUND" : "not found")}");
            LogAlways($"  ninZaAAATrendSync: {(aaaTrendSync != null ? "FOUND" : "not found")}");
            LogAlways($"  AIQ_1 (native):    {(nativeAiq1 != null ? "FOUND" : "not found")}");
            LogAlways($"  AIQ_SuperBands:    {(nativeAiqSuperBands != null ? "FOUND" : "not found")}");
            
            LogAlways($"--- Indicator Sources (Priority: ninZa/native > chart > hosted) ---");
            LogAlways($"  RubyRiver:    {(rubyRiver != null ? "ninZa" : (useChartRR ? "CHART" : "hosted"))}");
            LogAlways($"  DragonTrend:  {(dragonTrend != null ? "ninZa" : (useChartDT ? "CHART" : "hosted"))}");
            LogAlways($"  VIDYAPro:     {(vidyaPro != null ? "ninZa" : (useChartVY ? "CHART" : "hosted"))}");
            LogAlways($"  EasyTrend:    {(easyTrend != null ? "ninZa" : (useChartET ? "CHART" : "hosted"))}");
            LogAlways($"  SolarWave:    {(solarWave != null ? "ninZa" : (useChartSW ? "CHART" : "hosted"))}");
            LogAlways($"  T3Pro:        {(ninZaT3Pro != null ? "ninZa" : (useChartT3P ? "CHART" : "hosted"))}");
            LogAlways($"  AAATrendSync: {(aaaTrendSync != null ? "ninZa" : (useChartAAA ? "CHART" : "N/A"))}");
            LogAlways($"  AIQ_1:        {(useNativeAiq1 ? "NATIVE" : (useChartAiq1 ? "CHART" : "hosted"))}");
            LogAlways($"  SuperBands:   {(useNativeAiqSB ? "NATIVE" : (useChartSB ? "CHART" : "N/A"))}");
            LogAlways($"--------------------------------");
        }
        #endregion
        
        #region Indicator Accessors
        private bool GetBool(object o, FieldInfo f) { try { return o != null && f != null && (bool)f.GetValue(o); } catch { return false; } }
        private double GetDbl(object o, FieldInfo f) { try { return o != null && f != null ? (double)f.GetValue(o) : 0; } catch { return 0; } }
        private int GetInt(object o, FieldInfo f) { try { return o != null && f != null ? (int)f.GetValue(o) : 0; } catch { return 0; } }
        private bool GetChartBool(object o, PropertyInfo p) { try { return o != null && p != null && (bool)p.GetValue(o); } catch { return false; } }
        private double GetChartDbl(object o, PropertyInfo p) { try { return o != null && p != null ? (double)p.GetValue(o) : 0; } catch { return 0; } }
        private int GetChartInt(object o, PropertyInfo p) { try { return o != null && p != null ? (int)p.GetValue(o) : 0; } catch { return 0; } }

        [Browsable(false)] public bool RR_IsUp => rubyRiver != null ? GetBool(rubyRiver, rrIsUptrend) : (useChartRR ? GetChartBool(chartRubyRiverEquiv, rrEquivIsUptrend) : (rubyRiverEquivalent?.IsUptrend ?? false));
        [Browsable(false)] public bool VY_IsUp => vidyaPro != null ? GetBool(vidyaPro, vyIsUptrend) : (useChartVY ? GetChartBool(chartVidyaProEquiv, vyEquivIsUptrend) : (vidyaProEquivalent?.IsUptrend ?? false));
        [Browsable(false)] public bool ET_IsUp => easyTrend != null ? GetBool(easyTrend, etIsUptrend) : (useChartET ? GetChartBool(chartEasyTrendEquiv, etEquivIsUptrend) : (easyTrendEquivalent?.IsUptrend ?? false));
        [Browsable(false)] public double DT_Signal => dragonTrend != null ? GetDbl(dragonTrend, dtPrevSignal) : (useChartDT ? GetChartDbl(chartDragonTrendEquiv, dtEquivPrevSignal) : (dragonTrendEquivalent?.PrevSignal ?? 0));
        [Browsable(false)] public bool DT_IsUp => DT_Signal > 0;
        [Browsable(false)] public bool DT_IsDown => DT_Signal < 0;
        [Browsable(false)] public bool SW_IsUp => solarWave != null ? GetBool(solarWave, swIsUptrend) : (useChartSW ? GetChartBool(chartSolarWaveEquiv, swEquivIsUptrend) : (solarWaveEquivalent?.IsUptrend ?? false));
        [Browsable(false)] public int SW_Count => solarWave != null ? GetInt(solarWave, swCountWave) : (useChartSW ? GetChartInt(chartSolarWaveEquiv, swEquivCountWave) : (solarWaveEquivalent?.CountWave ?? 0));
        [Browsable(false)] public bool T3P_IsUp => ninZaT3Pro != null ? GetBool(ninZaT3Pro, t3pIsUptrend) : (useChartT3P ? GetChartBool(chartT3ProEquiv, t3pEquivIsUptrend) : (t3ProEquivalent?.IsUptrend ?? false));
        [Browsable(false)] public bool AAA_IsUp => aaaTrendSync != null ? GetBool(aaaTrendSync, aaaIsUptrend) : (useChartAAA ? GetChartBool(chartAAATrendSyncEquiv, aaaEquivIsUptrend) : false);
        [Browsable(false)] public bool AAA_Available => aaaTrendSync != null || useChartAAA;
        
        [Browsable(false)] public bool SB_IsUp 
        {
            get 
            {
                if (useNativeAiqSB)
                {
                    try 
                    { 
                        object val = nativeAiqSBIsUptrend.GetValue(nativeAiqSuperBands);
                        if (val is bool boolVal) return boolVal;
                        if (val is int intVal) return intVal > 0;
                        return false;
                    } 
                    catch { return false; }
                }
                if (useChartSB)
                    return GetChartBool(chartAiqSuperBandsEquiv, sbEquivIsUptrend);
                return false;
            }
        }
        [Browsable(false)] public bool SB_Available => useNativeAiqSB || useChartSB;
        
        [Browsable(false)] public bool AIQ1_IsUp 
        {
            get 
            {
                if (useNativeAiq1)
                    return GetInt(nativeAiq1, nativeAiq1TrendState) > 0;
                if (useChartAiq1)
                    return GetChartBool(chartAiq1Equivalent, aiq1IsUptrend);
                return aiq1Equivalent?.IsUptrend ?? false;
            }
        }
        #endregion
        
        #region Confluence and Confirmation
        private (int bull, int bear, int total) GetConfluence()
        {
            int bull = 0, bear = 0, total = 0;
            
            if (UseRubyRiver) { total++; if (RR_IsUp) bull++; else bear++; }
            if (UseDragonTrend) { total++; if (DT_IsUp) bull++; else if (DT_IsDown) bear++; }
            if (UseSolarWave) { total++; if (SW_IsUp && SW_Count >= MinSolarWaveCount) bull++; else if (!SW_IsUp && SW_Count <= -MinSolarWaveCount) bear++; }
            if (UseVIDYAPro) { total++; if (VY_IsUp) bull++; else bear++; }
            if (UseEasyTrend) { total++; if (ET_IsUp) bull++; else bear++; }
            if (UseT3Pro) { total++; if (T3P_IsUp) bull++; else bear++; }
            if (UseAAATrendSync && AAA_Available) { total++; if (AAA_IsUp) bull++; else bear++; }
            if (UseAIQSuperBands && SB_Available) { total++; if (SB_IsUp) bull++; else bear++; }
            
            return (bull, bear, total);
        }
        
        private string GetBullishConfirmation()
        {
            // Check which indicator just flipped bullish to confirm the LONG signal
            if (UseRubyRiver && RR_IsUp && !prevRR_IsUp) return "RR";
            if (UseDragonTrend && DT_IsUp && !prevDT_IsUp) return "DT";
            if (UseVIDYAPro && VY_IsUp && !prevVY_IsUp) return "VY";
            if (UseEasyTrend && ET_IsUp && !prevET_IsUp) return "ET";
            if (UseSolarWave && SW_IsUp && !prevSW_IsUp) return "SW";
            if (UseT3Pro && T3P_IsUp && !prevT3P_IsUp) return "T3P";
            if (UseAAATrendSync && AAA_Available && AAA_IsUp && !prevAAA_IsUp) return "AAA";
            if (UseAIQSuperBands && SB_Available && SB_IsUp && !prevSB_IsUp) return "SB";
            
            // If no flip detected, check if any indicator is confirming (already bullish)
            if (UseRubyRiver && RR_IsUp) return "RR";
            if (UseDragonTrend && DT_IsUp) return "DT";
            if (UseVIDYAPro && VY_IsUp) return "VY";
            if (UseEasyTrend && ET_IsUp) return "ET";
            if (UseSolarWave && SW_IsUp) return "SW";
            if (UseT3Pro && T3P_IsUp) return "T3P";
            if (UseAAATrendSync && AAA_Available && AAA_IsUp) return "AAA";
            if (UseAIQSuperBands && SB_Available && SB_IsUp) return "SB";
            
            return null;
        }
        
        private string GetBearishConfirmation()
        {
            // Check which indicator just flipped bearish to confirm the SHORT signal
            if (UseRubyRiver && !RR_IsUp && prevRR_IsUp) return "RR";
            if (UseDragonTrend && DT_IsDown && prevDT_IsUp) return "DT";
            if (UseVIDYAPro && !VY_IsUp && prevVY_IsUp) return "VY";
            if (UseEasyTrend && !ET_IsUp && prevET_IsUp) return "ET";
            if (UseSolarWave && !SW_IsUp && prevSW_IsUp) return "SW";
            if (UseT3Pro && !T3P_IsUp && prevT3P_IsUp) return "T3P";
            if (UseAAATrendSync && AAA_Available && !AAA_IsUp && prevAAA_IsUp) return "AAA";
            if (UseAIQSuperBands && SB_Available && !SB_IsUp && prevSB_IsUp) return "SB";
            
            // If no flip detected, check if any indicator is confirming (already bearish)
            if (UseRubyRiver && !RR_IsUp) return "RR";
            if (UseDragonTrend && DT_IsDown) return "DT";
            if (UseVIDYAPro && !VY_IsUp) return "VY";
            if (UseEasyTrend && !ET_IsUp) return "ET";
            if (UseSolarWave && !SW_IsUp) return "SW";
            if (UseT3Pro && !T3P_IsUp) return "T3P";
            if (UseAAATrendSync && AAA_Available && !AAA_IsUp) return "AAA";
            if (UseAIQSuperBands && SB_Available && !SB_IsUp) return "SB";
            
            return null;
        }
        #endregion
        
        #region OnBarUpdate
        protected override void OnBarUpdate()
        {
            if (CurrentBar < 20 || !indicatorsReady) return;
            
            DateTime barTime = Time[0];
            
            // Write CSV log row (every bar if enabled)
            WriteCSVRow(barTime);
            
            // BAR detail logging
            if (LogBarDetails)
            {
                var (bull, bear, total) = GetConfluence();
                PrintAndLog($"[BAR {CurrentBar}] {barTime:HH:mm:ss} | O={Open[0]:F2} H={High[0]:F2} L={Low[0]:F2} C={Close[0]:F2} | AIQ1={Ts(AIQ1_IsUp)} RR={Ts(RR_IsUp)} DT={DT_Signal:0} VY={Ts(VY_IsUp)} ET={Ts(ET_IsUp)} SW={SW_Count} T3P={Ts(T3P_IsUp)} AAA={Ts(AAA_IsUp)} SB={Ts(SB_IsUp)} Bull={bull} Bear={bear}");
            }
            
            // Cooldown tracking
            if (barsSinceLastSignal >= 0)
                barsSinceLastSignal++;
            
            bool inCooldown = false;
            string cooldownStatus = "";
            
            if (UseTimeBasedCooldown)
            {
                if (lastSignalTime != DateTime.MinValue)
                {
                    double secondsSinceSignal = (barTime - lastSignalTime).TotalSeconds;
                    inCooldown = secondsSinceSignal < CooldownSeconds;
                    if (inCooldown)
                        cooldownStatus = $"{secondsSinceSignal:F0}s/{CooldownSeconds}s";
                }
            }
            else
            {
                inCooldown = CooldownBars > 0 && barsSinceLastSignal >= 0 && barsSinceLastSignal < CooldownBars;
                if (inCooldown)
                    cooldownStatus = $"{barsSinceLastSignal}/{CooldownBars}";
            }
            
            // Detect Yellow/Orange squares (AIQ1 flips)
            bool yellowSquareAppeared = AIQ1_IsUp && !prevAIQ1_IsUp && !isFirstBar;
            bool orangeSquareAppeared = !AIQ1_IsUp && prevAIQ1_IsUp && !isFirstBar;
            
            if (yellowSquareAppeared)
            {
                barsSinceYellowSquare = 0;
                barsSinceOrangeSquare = -1;
                if (inCooldown)
                    PrintAndLog($"🟨 Yellow Square @ {barTime:HH:mm:ss} | BLOCKED by cooldown ({cooldownStatus})");
                else
                    PrintAndLog($"🟨 Yellow Square @ {barTime:HH:mm:ss} | LONG window opened (0/{MaxBarsAfterYellowSquare})");
            }
            else if (orangeSquareAppeared)
            {
                barsSinceOrangeSquare = 0;
                barsSinceYellowSquare = -1;
                if (inCooldown)
                    PrintAndLog($"🟧 Orange Square @ {barTime:HH:mm:ss} | BLOCKED by cooldown ({cooldownStatus})");
                else
                    PrintAndLog($"🟧 Orange Square @ {barTime:HH:mm:ss} | SHORT window opened (0/{MaxBarsAfterYellowSquare})");
            }
            else if (barsSinceYellowSquare >= 0)
            {
                barsSinceYellowSquare++;
                if (barsSinceYellowSquare > MaxBarsAfterYellowSquare)
                {
                    PrintAndLog($"LONG window expired @ {barTime:HH:mm:ss} | No confirmation within {MaxBarsAfterYellowSquare} bars");
                    barsSinceYellowSquare = -1;
                }
            }
            else if (barsSinceOrangeSquare >= 0)
            {
                barsSinceOrangeSquare++;
                if (barsSinceOrangeSquare > MaxBarsAfterYellowSquare)
                {
                    PrintAndLog($"SHORT window expired @ {barTime:HH:mm:ss} | No confirmation within {MaxBarsAfterYellowSquare} bars");
                    barsSinceOrangeSquare = -1;
                }
            }
            
            UpdatePanel();
            
            if (inCooldown)
            {
                prevAIQ1_IsUp = AIQ1_IsUp;
                isFirstBar = false;
                UpdatePreviousStates();
                return;
            }
            
            // Check for LONG signal confirmation
            if (barsSinceYellowSquare >= 0 && barsSinceYellowSquare <= MaxBarsAfterYellowSquare)
            {
                string confirmingIndicator = GetBullishConfirmation();
                
                if (confirmingIndicator != null)
                {
                    var (bull, bear, total) = GetConfluence();
                    
                    if (bull >= MinConfluenceRequired)
                    {
                        LogSignal("LONG", "YellowSquare+" + confirmingIndicator, barTime, bull, total);
                        UpdateSignalDisplay("YellowSquare+" + confirmingIndicator, bull, total, barTime, true);
                        
                        barsSinceYellowSquare = -1;
                        barsSinceLastSignal = 0;
                        lastSignalTime = barTime;
                    }
                    else
                    {
                        PrintAndLog($"{confirmingIndicator} confirmed but confluence {bull}/{total} < {MinConfluenceRequired} @ {barTime:HH:mm:ss}");
                    }
                }
            }
            
            // Check for SHORT signal confirmation
            if (barsSinceOrangeSquare >= 0 && barsSinceOrangeSquare <= MaxBarsAfterYellowSquare)
            {
                string confirmingIndicator = GetBearishConfirmation();
                
                if (confirmingIndicator != null)
                {
                    var (bull, bear, total) = GetConfluence();
                    
                    if (bear >= MinConfluenceRequired)
                    {
                        LogSignal("SHORT", "OrangeSquare+" + confirmingIndicator, barTime, bear, total);
                        UpdateSignalDisplay("OrangeSquare+" + confirmingIndicator, bear, total, barTime, false);
                        
                        barsSinceOrangeSquare = -1;
                        barsSinceLastSignal = 0;
                        lastSignalTime = barTime;
                    }
                    else
                    {
                        PrintAndLog($"{confirmingIndicator} confirmed but bear confluence {bear}/{total} < {MinConfluenceRequired} @ {barTime:HH:mm:ss}");
                    }
                }
            }
            
            UpdatePreviousStates();
            prevAIQ1_IsUp = AIQ1_IsUp;
            isFirstBar = false;
        }
        
        private void UpdatePreviousStates()
        {
            prevRR_IsUp = RR_IsUp;
            prevDT_IsUp = DT_IsUp;
            prevVY_IsUp = VY_IsUp;
            prevET_IsUp = ET_IsUp;
            prevSW_IsUp = SW_IsUp;
            prevT3P_IsUp = T3P_IsUp;
            prevAAA_IsUp = AAA_IsUp;
            prevSB_IsUp = SB_IsUp;
        }
        #endregion
        
        #region Panel UI
        private void CreateControlPanel()
        {
            try
            {
                string settingsDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8", "settings");
                panelSettingsFile = System.IO.Path.Combine(settingsDir, "ActiveNikiMonitor_PanelSettings.txt");
                panelTransform = new TranslateTransform(0, 0);
                
                LoadPanelSettings();

                controlPanel = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Background = new SolidColorBrush(Color.FromArgb(230, 30, 30, 40)),
                    Width = panelWidth,
                    MinWidth = minPanelWidth,
                    MinHeight = minPanelHeight,
                    RenderTransform = panelTransform,
                    Cursor = System.Windows.Input.Cursors.Arrow
                };
                
                controlPanel.MouseLeftButtonDown += Panel_MouseLeftButtonDown;
                controlPanel.MouseLeftButtonUp += Panel_MouseLeftButtonUp;
                controlPanel.MouseMove += Panel_MouseMove;
                controlPanel.MouseLeave += Panel_MouseLeave;

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 100)),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8)
                };
                
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = "ActiveNiki Monitor", FontWeight = FontWeights.Bold, Foreground = Brushes.Cyan, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) });
                lblSubtitle = new TextBlock { Foreground = Brushes.LightGray, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,6) };
                stack.Children.Add(lblSubtitle);
                
                stack.Children.Add(new TextBlock { Text = "── Confluence (8) ──", Foreground = Brushes.Gray, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,2,0,2) });
                
                // Indicators in alphabetical order (matching ActiveNikiTrader)
                stack.Children.Add(CreateRow("AAA TrendSync", ref chkAAASync, ref lblAAASync, UseAAATrendSync));
                stack.Children.Add(CreateRow("AIQ SuperBands", ref chkSuperBands, ref lblSuperBands, UseAIQSuperBands));
                stack.Children.Add(CreateRow("Dragon Trend", ref chkDragonTrend, ref lblDragonTrend, UseDragonTrend));
                stack.Children.Add(CreateRow("Easy Trend", ref chkEasyTrend, ref lblEasyTrend, UseEasyTrend));
                stack.Children.Add(CreateRow("Ruby River", ref chkRubyRiver, ref lblRubyRiver, UseRubyRiver));
                stack.Children.Add(CreateRow("Solar Wave", ref chkSolarWave, ref lblSolarWave, UseSolarWave));
                stack.Children.Add(CreateRow("T3 Pro", ref chkT3Pro, ref lblT3Pro, UseT3Pro));
                stack.Children.Add(CreateRow("VIDYA Pro", ref chkVIDYA, ref lblVIDYA, UseVIDYAPro));
                
                stack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(0,6,0,6) });
                stack.Children.Add(new TextBlock { Text = "── Trigger ──", Foreground = Brushes.Orange, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,2,0,2) });
                
                var aiqRow = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                aiqRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                aiqRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                lblAIQ1Name = new TextBlock { Text = "AIQ_1 (Yellow ■)", Foreground = Brushes.Yellow, FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(lblAIQ1Name, 0); aiqRow.Children.Add(lblAIQ1Name);
                lblAIQ1Status = new TextBlock { Text = "---", Foreground = Brushes.Gray, FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(lblAIQ1Status, 1); aiqRow.Children.Add(lblAIQ1Status);
                stack.Children.Add(aiqRow);
                
                lblWindowStatus = new TextBlock { Text = "Window: CLOSED", Foreground = Brushes.Gray, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,2,0,2) };
                stack.Children.Add(lblWindowStatus);
                
                stack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(0,6,0,6) });

                lblTriggerMode = new TextBlock { Text = $"Signal≥{MinConfluenceRequired} CD={CooldownBars}", Foreground = Brushes.LightGray, FontSize = 9 };
                lblTradeStatus = new TextBlock { Text = "Mode: Signal Only", Foreground = Brushes.Yellow, FontWeight = FontWeights.Bold, FontSize = 10, Margin = new Thickness(0,2,0,2) };
                lblSessionStats = new TextBlock { Text = "Signals: 0", Foreground = Brushes.LightGray, FontSize = 9 };

                stack.Children.Add(lblTriggerMode);
                stack.Children.Add(lblTradeStatus);
                stack.Children.Add(lblSessionStats);
                
                stack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(0,6,0,6) });
                
                signalBorder = new Border { BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(3), Padding = new Thickness(4) };
                lblLastSignal = new TextBlock { Text = "Waiting for Yellow ■...", Foreground = Brushes.Gray, FontSize = 9, TextWrapping = TextWrapping.Wrap };
                signalBorder.Child = lblLastSignal;
                stack.Children.Add(signalBorder);
                
                // Add resize grip indicator in bottom-right corner
                var resizeIndicator = new Canvas { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 4, 0, 0) };
                for (int i = 0; i < 3; i++)
                {
                    var line = new Line { X1 = 10 - i * 4, Y1 = 10, X2 = 10, Y2 = 10 - i * 4, Stroke = new SolidColorBrush(Color.FromRgb(120, 120, 140)), StrokeThickness = 1 };
                    resizeIndicator.Children.Add(line);
                }
                stack.Children.Add(resizeIndicator);
                
                border.Child = stack;
                controlPanel.Children.Add(border);

                UIElementCollection panelHolder = (ChartControl.Parent as Grid)?.Children;
                if (panelHolder != null) panelHolder.Add(controlPanel);
                panelActive = true;
                
                ApplyPanelConstraints();
            }
            catch (Exception ex) { Print($"Panel error: {ex.Message}"); }
        }
        
        private Grid CreateRow(string name, ref CheckBox chk, ref TextBlock lbl, bool isChecked)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });
            
            chk = new CheckBox { IsChecked = isChecked, VerticalAlignment = VerticalAlignment.Center };
            chk.Checked += OnChk; chk.Unchecked += OnChk;
            Grid.SetColumn(chk, 0); row.Children.Add(chk);
            
            var txt = new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3,0,0,0) };
            Grid.SetColumn(txt, 1); row.Children.Add(txt);
            
            lbl = new TextBlock { Text = "---", Foreground = Brushes.Gray, FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(lbl, 2); row.Children.Add(lbl);
            return row;
        }
        
        private void OnChk(object s, RoutedEventArgs e)
        {
            UseRubyRiver = chkRubyRiver?.IsChecked ?? false;
            UseDragonTrend = chkDragonTrend?.IsChecked ?? false;
            UseSolarWave = chkSolarWave?.IsChecked ?? false;
            UseVIDYAPro = chkVIDYA?.IsChecked ?? false;
            UseEasyTrend = chkEasyTrend?.IsChecked ?? false;
            UseT3Pro = chkT3Pro?.IsChecked ?? false;
            UseAAATrendSync = chkAAASync?.IsChecked ?? false;
            UseAIQSuperBands = chkSuperBands?.IsChecked ?? false;
        }
        
        private void RemoveControlPanel()
        {
            try
            {
                if (controlPanel != null && panelActive)
                {
                    controlPanel.MouseLeftButtonDown -= Panel_MouseLeftButtonDown;
                    controlPanel.MouseLeftButtonUp -= Panel_MouseLeftButtonUp;
                    controlPanel.MouseMove -= Panel_MouseMove;
                    controlPanel.MouseLeave -= Panel_MouseLeave;
                    UIElementCollection panelHolder = (ChartControl?.Parent as Grid)?.Children;
                    if (panelHolder != null && panelHolder.Contains(controlPanel))
                        panelHolder.Remove(controlPanel);
                    panelActive = false;
                }
            }
            catch { }
        }

        private ResizeEdge GetResizeEdge(Point mousePos)
        {
            double w = controlPanel.ActualWidth > 0 ? controlPanel.ActualWidth : panelWidth;
            double h = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : panelHeight;
            
            bool nearLeft = mousePos.X <= EdgeThreshold;
            bool nearRight = mousePos.X >= w - EdgeThreshold;
            bool nearTop = mousePos.Y <= EdgeThreshold;
            bool nearBottom = mousePos.Y >= h - EdgeThreshold;
            
            if (nearTop && nearLeft) return ResizeEdge.TopLeft;
            if (nearTop && nearRight) return ResizeEdge.TopRight;
            if (nearBottom && nearLeft) return ResizeEdge.BottomLeft;
            if (nearBottom && nearRight) return ResizeEdge.BottomRight;
            if (nearLeft) return ResizeEdge.Left;
            if (nearRight) return ResizeEdge.Right;
            if (nearTop) return ResizeEdge.Top;
            if (nearBottom) return ResizeEdge.Bottom;
            
            return ResizeEdge.None;
        }
        
        private System.Windows.Input.Cursor GetCursorForEdge(ResizeEdge edge)
        {
            switch (edge)
            {
                case ResizeEdge.Left:
                case ResizeEdge.Right:
                    return System.Windows.Input.Cursors.SizeWE;
                case ResizeEdge.Top:
                case ResizeEdge.Bottom:
                    return System.Windows.Input.Cursors.SizeNS;
                case ResizeEdge.TopLeft:
                case ResizeEdge.BottomRight:
                    return System.Windows.Input.Cursors.SizeNWSE;
                case ResizeEdge.TopRight:
                case ResizeEdge.BottomLeft:
                    return System.Windows.Input.Cursors.SizeNESW;
                default:
                    return System.Windows.Input.Cursors.Hand;
            }
        }

        private void Panel_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Point mousePos = e.GetPosition(controlPanel);
            ResizeEdge edge = GetResizeEdge(mousePos);
            
            if (edge != ResizeEdge.None)
            {
                currentResizeEdge = edge;
                isResizing = true;
                resizeStartMousePos = e.GetPosition(ChartControl?.Parent as UIElement);
                resizeStartWidth = controlPanel.ActualWidth > 0 ? controlPanel.ActualWidth : panelWidth;
                resizeStartHeight = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : panelHeight;
                resizeStartLeft = panelTransform.X;
                resizeStartTop = panelTransform.Y;
                controlPanel.CaptureMouse();
                e.Handled = true;
            }
            else
            {
                isDragging = true;
                dragStartPoint = e.GetPosition(ChartControl?.Parent as UIElement);
                dragStartPoint.X -= panelTransform.X;
                dragStartPoint.Y -= panelTransform.Y;
                controlPanel.CaptureMouse();
                e.Handled = true;
            }
        }

        private void Panel_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (isDragging || isResizing)
            {
                isDragging = false;
                isResizing = false;
                currentResizeEdge = ResizeEdge.None;
                controlPanel.ReleaseMouseCapture();
                SavePanelSettings();
                e.Handled = true;
            }
        }
        
        private void Panel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!isDragging && !isResizing)
            {
                controlPanel.Cursor = System.Windows.Input.Cursors.Arrow;
            }
        }

        private void Panel_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var parent = ChartControl?.Parent as FrameworkElement;
            if (parent == null) return;
            
            Point currentMousePos = e.GetPosition(parent);
            
            if (isResizing && currentResizeEdge != ResizeEdge.None)
            {
                double deltaX = currentMousePos.X - resizeStartMousePos.X;
                double deltaY = currentMousePos.Y - resizeStartMousePos.Y;
                
                double newWidth = resizeStartWidth;
                double newHeight = resizeStartHeight;
                double newLeft = resizeStartLeft;
                double newTop = resizeStartTop;
                
                switch (currentResizeEdge)
                {
                    case ResizeEdge.Right:
                        newWidth = resizeStartWidth + deltaX;
                        break;
                    case ResizeEdge.Left:
                        newWidth = resizeStartWidth - deltaX;
                        newLeft = resizeStartLeft + deltaX;
                        break;
                    case ResizeEdge.Bottom:
                        newHeight = resizeStartHeight + deltaY;
                        break;
                    case ResizeEdge.Top:
                        newHeight = resizeStartHeight - deltaY;
                        newTop = resizeStartTop + deltaY;
                        break;
                    case ResizeEdge.BottomRight:
                        double aspectRatio = resizeStartWidth / resizeStartHeight;
                        double avgDelta = (deltaX + deltaY) / 2;
                        newWidth = resizeStartWidth + avgDelta;
                        newHeight = newWidth / aspectRatio;
                        break;
                    case ResizeEdge.BottomLeft:
                        newWidth = resizeStartWidth - deltaX;
                        newLeft = resizeStartLeft + deltaX;
                        newHeight = resizeStartHeight + deltaY;
                        break;
                    case ResizeEdge.TopRight:
                        newWidth = resizeStartWidth + deltaX;
                        newHeight = resizeStartHeight - deltaY;
                        newTop = resizeStartTop + deltaY;
                        break;
                    case ResizeEdge.TopLeft:
                        newWidth = resizeStartWidth - deltaX;
                        newLeft = resizeStartLeft + deltaX;
                        newHeight = resizeStartHeight - deltaY;
                        newTop = resizeStartTop + deltaY;
                        break;
                }
                
                if (newWidth < minPanelWidth)
                {
                    if (currentResizeEdge == ResizeEdge.Left || currentResizeEdge == ResizeEdge.TopLeft || currentResizeEdge == ResizeEdge.BottomLeft)
                        newLeft = resizeStartLeft + (resizeStartWidth - minPanelWidth);
                    newWidth = minPanelWidth;
                }
                if (newHeight < minPanelHeight)
                {
                    if (currentResizeEdge == ResizeEdge.Top || currentResizeEdge == ResizeEdge.TopLeft || currentResizeEdge == ResizeEdge.TopRight)
                        newTop = resizeStartTop + (resizeStartHeight - minPanelHeight);
                    newHeight = minPanelHeight;
                }
                
                if (newLeft < 0) 
                {
                    newWidth = newWidth + newLeft;
                    newLeft = 0;
                }
                if (newTop < 0)
                {
                    newHeight = newHeight + newTop;
                    newTop = 0;
                }
                if (newLeft + newWidth > parent.ActualWidth)
                {
                    newWidth = parent.ActualWidth - newLeft;
                }
                if (newTop + newHeight > parent.ActualHeight)
                {
                    newHeight = parent.ActualHeight - newTop;
                }
                
                newWidth = Math.Max(newWidth, minPanelWidth);
                newHeight = Math.Max(newHeight, minPanelHeight);
                
                panelWidth = newWidth;
                panelHeight = newHeight;
                controlPanel.Width = newWidth;
                controlPanel.Height = newHeight;
                panelTransform.X = newLeft;
                panelTransform.Y = newTop;
                
                e.Handled = true;
            }
            else if (isDragging)
            {
                double newX = currentMousePos.X - dragStartPoint.X;
                double newY = currentMousePos.Y - dragStartPoint.Y;
                
                double w = controlPanel.ActualWidth > 0 ? controlPanel.ActualWidth : panelWidth;
                double h = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : panelHeight;
                
                newX = Math.Max(0, Math.Min(parent.ActualWidth - w, newX));
                newY = Math.Max(0, Math.Min(parent.ActualHeight - h, newY));
                
                panelTransform.X = newX;
                panelTransform.Y = newY;
                e.Handled = true;
            }
            else
            {
                Point mousePos = e.GetPosition(controlPanel);
                ResizeEdge edge = GetResizeEdge(mousePos);
                controlPanel.Cursor = GetCursorForEdge(edge);
            }
        }

        private void ApplyPanelConstraints()
        {
            var parent = ChartControl?.Parent as FrameworkElement;
            if (parent == null || controlPanel == null) return;
            
            double maxX = Math.Max(0, parent.ActualWidth - panelWidth);
            double maxY = Math.Max(0, parent.ActualHeight - panelHeight);
            
            panelTransform.X = Math.Max(0, Math.Min(maxX, panelTransform.X));
            panelTransform.Y = Math.Max(0, Math.Min(maxY, panelTransform.Y));
            
            controlPanel.Width = panelWidth;
            controlPanel.Height = panelHeight;
        }

        private void SavePanelSettings()
        {
            try
            {
                if (string.IsNullOrEmpty(panelSettingsFile)) return;
                string dir = System.IO.Path.GetDirectoryName(panelSettingsFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                
                double w = controlPanel.ActualWidth > 0 ? controlPanel.ActualWidth : panelWidth;
                double h = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : panelHeight;
                
                File.WriteAllText(panelSettingsFile, $"{panelTransform.X},{panelTransform.Y},{w},{h}");
            }
            catch { }
        }

        private void LoadPanelSettings()
        {
            try
            {
                if (string.IsNullOrEmpty(panelSettingsFile) || !File.Exists(panelSettingsFile)) return;
                string content = File.ReadAllText(panelSettingsFile);
                string[] parts = content.Split(',');
                
                if (parts.Length >= 2 && double.TryParse(parts[0], out double x) && double.TryParse(parts[1], out double y))
                {
                    panelTransform.X = x;
                    panelTransform.Y = y;
                }
                if (parts.Length >= 4 && double.TryParse(parts[2], out double w) && double.TryParse(parts[3], out double h))
                {
                    panelWidth = Math.Max(minPanelWidth, w);
                    panelHeight = Math.Max(minPanelHeight, h);
                }
            }
            catch { }
        }
        
        private int GetEnabledCount() => (UseRubyRiver?1:0)+(UseDragonTrend?1:0)+(UseSolarWave?1:0)+(UseVIDYAPro?1:0)+(UseEasyTrend?1:0)+(UseT3Pro?1:0)+(UseAAATrendSync && AAA_Available?1:0)+(UseAIQSuperBands && SB_Available?1:0);
        
        private void UpdatePanel()
        {
            if (!panelActive || ChartControl == null) return;
            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                int enabled = GetEnabledCount();
                if (lblSubtitle != null)
                    lblSubtitle.Text = enabled == 0 ? "No indicators" : $"Min {MinConfluenceRequired}/{enabled} for signal";

                // AAA TrendSync
                if (lblAAASync != null)
                {
                    if (!UseAAATrendSync) { lblAAASync.Text = "OFF"; lblAAASync.Foreground = Brushes.Gray; }
                    else if (!AAA_Available) { lblAAASync.Text = "N/A"; lblAAASync.Foreground = Brushes.DarkGray; }
                    else { lblAAASync.Text = AAA_IsUp ? "UP" : "DN"; lblAAASync.Foreground = AAA_IsUp ? Brushes.Lime : Brushes.Red; }
                }
                // AIQ SuperBands
                if (lblSuperBands != null)
                {
                    if (!UseAIQSuperBands) { lblSuperBands.Text = "OFF"; lblSuperBands.Foreground = Brushes.Gray; }
                    else if (!SB_Available) { lblSuperBands.Text = "N/A"; lblSuperBands.Foreground = Brushes.DarkGray; }
                    else { lblSuperBands.Text = SB_IsUp ? "UP" : "DN"; lblSuperBands.Foreground = SB_IsUp ? Brushes.Lime : Brushes.Red; }
                }
                UpdLbl(lblDragonTrend, DT_IsUp, UseDragonTrend);
                UpdLbl(lblEasyTrend, ET_IsUp, UseEasyTrend);
                UpdLbl(lblRubyRiver, RR_IsUp, UseRubyRiver);
                UpdLbl(lblSolarWave, SW_IsUp, UseSolarWave);
                UpdLbl(lblT3Pro, T3P_IsUp, UseT3Pro);
                UpdLbl(lblVIDYA, VY_IsUp, UseVIDYAPro);
                
                if (lblAIQ1Status != null)
                {
                    lblAIQ1Status.Text = AIQ1_IsUp ? "UP" : "DN";
                    lblAIQ1Status.Foreground = AIQ1_IsUp ? Brushes.Lime : Brushes.Red;
                }
                
                // Dynamic trigger label - shows Yellow/Orange based on current state
                if (lblAIQ1Name != null)
                {
                    bool longWindowOpen = barsSinceYellowSquare >= 0 && barsSinceYellowSquare <= MaxBarsAfterYellowSquare;
                    bool shortWindowOpen = barsSinceOrangeSquare >= 0 && barsSinceOrangeSquare <= MaxBarsAfterYellowSquare;
                    var (bullConf, bearConf, _) = GetConfluence();
                    
                    if (longWindowOpen)
                    {
                        lblAIQ1Name.Text = "AIQ_1 (Yellow ■)";
                        lblAIQ1Name.Foreground = Brushes.Yellow;
                    }
                    else if (shortWindowOpen)
                    {
                        lblAIQ1Name.Text = "AIQ_1 (Orange ■)";
                        lblAIQ1Name.Foreground = Brushes.Orange;
                    }
                    else if (bearConf >= MinConfluenceRequired)
                    {
                        lblAIQ1Name.Text = "AIQ_1 (Orange ■)";
                        lblAIQ1Name.Foreground = Brushes.Orange;
                    }
                    else if (bullConf >= MinConfluenceRequired)
                    {
                        lblAIQ1Name.Text = "AIQ_1 (Yellow ■)";
                        lblAIQ1Name.Foreground = Brushes.Yellow;
                    }
                    else
                    {
                        // Low confluence - show based on current AIQ1 state
                        lblAIQ1Name.Text = AIQ1_IsUp ? "AIQ_1 (Yellow ■)" : "AIQ_1 (Orange ■)";
                        lblAIQ1Name.Foreground = Brushes.Gray;
                    }
                }
                
                // Window status
                if (lblWindowStatus != null)
                {
                    bool inCooldown = false;
                    string cooldownText = "";
                    
                    if (UseTimeBasedCooldown && lastSignalTime != DateTime.MinValue)
                    {
                        double secondsSinceSignal = (DateTime.Now - lastSignalTime).TotalSeconds;
                        inCooldown = secondsSinceSignal < CooldownSeconds;
                        if (inCooldown)
                            cooldownText = $"🕐 Cooldown ({secondsSinceSignal:F0}s/{CooldownSeconds}s)";
                    }
                    else
                    {
                        inCooldown = CooldownBars > 0 && barsSinceLastSignal >= 0 && barsSinceLastSignal < CooldownBars;
                        if (inCooldown)
                            cooldownText = $"🕐 Cooldown ({barsSinceLastSignal}/{CooldownBars})";
                    }
                    
                    if (inCooldown)
                    {
                        lblWindowStatus.Text = cooldownText;
                        lblWindowStatus.Foreground = Brushes.Yellow;
                    }
                    else if (barsSinceYellowSquare >= 0 && barsSinceYellowSquare <= MaxBarsAfterYellowSquare)
                    {
                        lblWindowStatus.Text = $"⚡ LONG Window ({barsSinceYellowSquare}/{MaxBarsAfterYellowSquare})";
                        lblWindowStatus.Foreground = Brushes.Lime;
                    }
                    else if (barsSinceOrangeSquare >= 0 && barsSinceOrangeSquare <= MaxBarsAfterYellowSquare)
                    {
                        lblWindowStatus.Text = $"⚡ SHORT Window ({barsSinceOrangeSquare}/{MaxBarsAfterYellowSquare})";
                        lblWindowStatus.Foreground = Brushes.Orange;
                    }
                    else
                    {
                        lblWindowStatus.Text = "Window: CLOSED";
                        lblWindowStatus.Foreground = Brushes.Gray;
                    }
                }

                var (bull, bear, total) = GetConfluence();
                if (lblSessionStats != null) lblSessionStats.Text = $"Signals: {signalCount} | Bull:{bull} Bear:{bear}/{total}";

                if (lblLastSignal != null && signalBorder != null)
                {
                    bool longWindowOpen = barsSinceYellowSquare >= 0 && barsSinceYellowSquare <= MaxBarsAfterYellowSquare;
                    bool shortWindowOpen = barsSinceOrangeSquare >= 0 && barsSinceOrangeSquare <= MaxBarsAfterYellowSquare;
                    
                    if (total == 0)
                    {
                        lblLastSignal.Text = "No indicators selected";
                        lblLastSignal.Foreground = Brushes.Gray;
                        lblLastSignal.FontWeight = FontWeights.Normal;
                        signalBorder.BorderBrush = Brushes.Transparent;
                        signalBorder.Background = Brushes.Transparent;
                    }
                    else if (longWindowOpen && RR_IsUp && bull >= MinConfluenceRequired)
                    {
                        lblLastSignal.Text = $"🔔 READY: LONG ({bull}/{total})";
                        lblLastSignal.FontWeight = FontWeights.Bold;
                        lblLastSignal.Foreground = Brushes.Lime;
                        signalBorder.BorderBrush = Brushes.Lime;
                        signalBorder.Background = new SolidColorBrush(Color.FromArgb(60, 0, 255, 0));
                    }
                    else if (shortWindowOpen && !RR_IsUp && bear >= MinConfluenceRequired)
                    {
                        lblLastSignal.Text = $"🔔 READY: SHORT ({bear}/{total})";
                        lblLastSignal.FontWeight = FontWeights.Bold;
                        lblLastSignal.Foreground = Brushes.Orange;
                        signalBorder.BorderBrush = Brushes.Orange;
                        signalBorder.Background = new SolidColorBrush(Color.FromArgb(60, 255, 165, 0));
                    }
                    else if (longWindowOpen)
                    {
                        string waiting = !RR_IsUp ? "RR not UP" : $"Bull {bull}/{MinConfluenceRequired}";
                        lblLastSignal.Text = $"LONG window - {waiting}";
                        lblLastSignal.FontWeight = FontWeights.Normal;
                        lblLastSignal.Foreground = Brushes.Yellow;
                        signalBorder.BorderBrush = Brushes.Yellow;
                        signalBorder.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 0));
                    }
                    else if (shortWindowOpen)
                    {
                        string waiting = RR_IsUp ? "RR not DN" : $"Bear {bear}/{MinConfluenceRequired}";
                        lblLastSignal.Text = $"SHORT window - {waiting}";
                        lblLastSignal.FontWeight = FontWeights.Normal;
                        lblLastSignal.Foreground = Brushes.Orange;
                        signalBorder.BorderBrush = Brushes.Orange;
                        signalBorder.Background = new SolidColorBrush(Color.FromArgb(30, 255, 165, 0));
                    }
                    else if (bull >= MinConfluenceRequired)
                    {
                        lblLastSignal.Text = $"Bull OK ({bull}/{total})\nWaiting for Yellow ■...";
                        lblLastSignal.FontWeight = FontWeights.Normal;
                        lblLastSignal.Foreground = Brushes.Lime;
                        signalBorder.BorderBrush = Brushes.Lime;
                        signalBorder.Background = Brushes.Transparent;
                    }
                    else if (bear >= MinConfluenceRequired)
                    {
                        lblLastSignal.Text = $"Bear OK ({bear}/{total})\nWaiting for Orange ■...";
                        lblLastSignal.FontWeight = FontWeights.Normal;
                        lblLastSignal.Foreground = Brushes.Orange;
                        signalBorder.BorderBrush = Brushes.Orange;
                        signalBorder.Background = Brushes.Transparent;
                    }
                    else
                    {
                        lblLastSignal.Text = $"Low confluence (Bull:{bull} Bear:{bear})";
                        lblLastSignal.FontWeight = FontWeights.Normal;
                        lblLastSignal.Foreground = Brushes.Gray;
                        signalBorder.BorderBrush = Brushes.Gray;
                        signalBorder.Background = Brushes.Transparent;
                    }
                }
            });
        }
        
        private void UpdateSignalDisplay(string trigger, int aligned, int total, DateTime t, bool isLong)
        {
            signalCount++;
            string dir = isLong ? "LONG" : "SHORT";
            lastSignalText = $"{dir} @ {aligned}/{total} [{trigger}] {t:HH:mm:ss}";
            if (EnableSoundAlert)
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
        }
        
        private void UpdLbl(TextBlock l, bool? v, bool en)
        {
            if (l == null) return;
            if (!en) { l.Text = "OFF"; l.Foreground = Brushes.Gray; }
            else if (!v.HasValue) { l.Text = "MIX"; l.Foreground = Brushes.Yellow; }
            else { l.Text = v.Value ? "UP" : "DN"; l.Foreground = v.Value ? Brushes.Lime : Brushes.Red; }
        }
        #endregion
        #region Logging
        private void LogSignal(string dir, string trigger, DateTime t, int confluenceCount, int total)
        {
            double askPrice = Close[0];
            double bidPrice = Close[0];
            
            int barsAfterSquare = dir == "LONG" ? barsSinceYellowSquare : barsSinceOrangeSquare;
            string squareType = dir == "LONG" ? "Yellow■" : "Orange■";
            
            PrintAndLog($"");
            PrintAndLog($"╔═══════════════════════════════════════════════════════════════╗");
            PrintAndLog($"║  *** {dir} SIGNAL @ {t:HH:mm:ss} ***");
            PrintAndLog($"╠═══════════════════════════════════════════════════════════════╣");
            PrintAndLog($"║  Instrument: {Instrument.FullName}");
            PrintAndLog($"║  Price: {Close[0]:F2}");
            PrintAndLog($"╠═══════════════════════════════════════════════════════════════╣");
            PrintAndLog($"║  Trigger: {trigger}");
            PrintAndLog($"║  Confluence: {confluenceCount}/{total}");
            PrintAndLog($"║  RR={Ts(RR_IsUp)} DT={DT_Signal:F0} VY={Ts(VY_IsUp)} ET={Ts(ET_IsUp)} SW={SW_Count} T3P={Ts(T3P_IsUp)} AAA={Ts(AAA_IsUp)} SB={Ts(SB_IsUp)}");
            PrintAndLog($"║  AIQ1={Ts(AIQ1_IsUp)} | Bars after {squareType}: {barsAfterSquare}");
            PrintAndLog($"╚═══════════════════════════════════════════════════════════════╝");
        }
        
        private string Ts(bool up) => up ? "UP" : "DN";
        
        private void InitializeLogFile()
        {
            try
            {
                string dir = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "log");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                logFilePath = System.IO.Path.Combine(dir, $"ActiveNikiMonitor_{DateTime.Now:yyyy-MM-dd}_{chartSessionId}.txt");
                logWriter = new StreamWriter(logFilePath, true) { AutoFlush = true };
                logWriter.WriteLine($"\n=== ActiveNikiMonitor Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                logWriter.WriteLine($"    8-indicator confluence filter");
                logWriter.WriteLine($"    Signal Filter: MinConf={MinConfluenceRequired}/8, MaxBars={MaxBarsAfterYellowSquare}, Cooldown={CooldownBars}");
                logWriter.WriteLine($"    Mode: Signal Only (no auto trading)");
                logWriter.WriteLine($"    LONG:  Yellow■ (AIQ1 UP) → Any indicator confirms → Bull Confluence ≥ {MinConfluenceRequired}");
                logWriter.WriteLine($"    SHORT: Orange■ (AIQ1 DN) → Any indicator confirms → Bear Confluence ≥ {MinConfluenceRequired}\n");
            }
            catch { }
        }
        
        private void CloseLogFile()
        {
            try
            {
                logWriter?.WriteLine($"\n=== Session Ended: {DateTime.Now:HH:mm:ss} | Signals: {signalCount} ===");
                logWriter?.Close();
            }
            catch { }
        }
        
        private void PrintAndLog(string msg)
        {
            Print(msg);
            if (logWriter != null)
                try { logWriter.WriteLine($"{DateTime.Now:HH:mm:ss} | {msg}"); } catch { }
        }
        
        private void LogAlways(string msg)
        {
            Print(msg);
            if (logWriter != null)
                try { logWriter.WriteLine($"{DateTime.Now:HH:mm:ss} | {msg}"); } catch { }
        }
        #endregion
        
        #region CSV Logging
        private void InitializeCSVLog()
        {
            if (!EnableIndicatorCSVLog) return;
            try
            {
                string dir = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "log");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                csvLogFilePath = System.IO.Path.Combine(dir, $"IndicatorValues_{DateTime.Now:yyyy-MM-dd}_{chartSessionId}.csv");
                csvWriter = new StreamWriter(csvLogFilePath, false) { AutoFlush = true };
                csvWriter.WriteLine("BarTime,Close,AIQ1_IsUp,RR_IsUp,DT_Signal,VY_IsUp,ET_IsUp,SW_IsUp,SW_Count,T3P_IsUp,AAA_IsUp,SB_IsUp,BullConf,BearConf,Source");
                LogAlways($"📊 CSV Log: {csvLogFilePath}");
            }
            catch (Exception ex) { Print($"CSV Init Error: {ex.Message}"); }
        }
        
        private void WriteCSVRow(DateTime barTime)
        {
            if (csvWriter == null || !EnableIndicatorCSVLog) return;
            try
            {
                var (bull, bear, total) = GetConfluence();
                string source = GetIndicatorSourceSummary();
                csvWriter.WriteLine($"{barTime:yyyy-MM-dd HH:mm:ss},{Close[0]:F2},{B2I(AIQ1_IsUp)},{B2I(RR_IsUp)},{DT_Signal:F2},{B2I(VY_IsUp)},{B2I(ET_IsUp)},{B2I(SW_IsUp)},{SW_Count},{B2I(T3P_IsUp)},{B2I(AAA_IsUp)},{B2I(SB_IsUp)},{bull},{bear},{source}");
            }
            catch { }
        }
        
        private int B2I(bool b) => b ? 1 : 0;
        
        private string GetIndicatorSourceSummary()
        {
            string aiq = useNativeAiq1 ? "N" : (useChartAiq1 ? "C" : "H");
            string rr = rubyRiver != null ? "N" : (useChartRR ? "C" : "H");
            string dt = dragonTrend != null ? "N" : (useChartDT ? "C" : "H");
            string vy = vidyaPro != null ? "N" : (useChartVY ? "C" : "H");
            string et = easyTrend != null ? "N" : (useChartET ? "C" : "H");
            string sw = solarWave != null ? "N" : (useChartSW ? "C" : "H");
            string t3 = ninZaT3Pro != null ? "N" : (useChartT3P ? "C" : "H");
            string aaa = aaaTrendSync != null ? "N" : (useChartAAA ? "C" : "-");
            string sb = useNativeAiqSB ? "N" : (useChartSB ? "C" : "-");
            return $"AIQ:{aiq}|RR:{rr}|DT:{dt}|VY:{vy}|ET:{et}|SW:{sw}|T3:{t3}|AAA:{aaa}|SB:{sb}";
        }
        
        private void CloseCSVLog()
        {
            try { csvWriter?.Close(); } catch { }
        }
        #endregion
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ActiveNikiMonitor[] cacheActiveNikiMonitor;
		public ActiveNikiMonitor ActiveNikiMonitor(int minConfluenceRequired, int maxBarsAfterYellowSquare, int minSolarWaveCount, int cooldownBars, bool useTimeBasedCooldown, int cooldownSeconds, bool useRubyRiver, bool useDragonTrend, bool useSolarWave, bool useVIDYAPro, bool useEasyTrend, bool useT3Pro, bool useAAATrendSync, bool useAIQSuperBands, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, bool t3ProChaosSmoothingEnabled, int t3ProChaosSmoothingPeriod, bool t3ProFilterEnabled, double t3ProFilterMultiplier, int vIDYAPeriod, int vIDYAVolatilityPeriod, bool vIDYASmoothingEnabled, int vIDYASmoothingPeriod, bool vIDYAFilterEnabled, double vIDYAFilterMultiplier, int easyTrendPeriod, bool easyTrendSmoothingEnabled, int easyTrendSmoothingPeriod, bool easyTrendFilterEnabled, double easyTrendFilterMultiplier, int easyTrendATRPeriod, int rubyRiverMAPeriod, bool rubyRiverSmoothingEnabled, int rubyRiverSmoothingPeriod, double rubyRiverOffsetMultiplier, int rubyRiverOffsetPeriod, int dragonTrendPeriod, bool dragonTrendSmoothingEnabled, int dragonTrendSmoothingPeriod, int solarWaveATRPeriod, double solarWaveTrendMultiplier, double solarWaveStopMultiplier, bool enableSoundAlert, bool enableIndicatorCSVLog, bool logBarDetails)
		{
			return ActiveNikiMonitor(Input, minConfluenceRequired, maxBarsAfterYellowSquare, minSolarWaveCount, cooldownBars, useTimeBasedCooldown, cooldownSeconds, useRubyRiver, useDragonTrend, useSolarWave, useVIDYAPro, useEasyTrend, useT3Pro, useAAATrendSync, useAIQSuperBands, t3ProPeriod, t3ProTCount, t3ProVFactor, t3ProChaosSmoothingEnabled, t3ProChaosSmoothingPeriod, t3ProFilterEnabled, t3ProFilterMultiplier, vIDYAPeriod, vIDYAVolatilityPeriod, vIDYASmoothingEnabled, vIDYASmoothingPeriod, vIDYAFilterEnabled, vIDYAFilterMultiplier, easyTrendPeriod, easyTrendSmoothingEnabled, easyTrendSmoothingPeriod, easyTrendFilterEnabled, easyTrendFilterMultiplier, easyTrendATRPeriod, rubyRiverMAPeriod, rubyRiverSmoothingEnabled, rubyRiverSmoothingPeriod, rubyRiverOffsetMultiplier, rubyRiverOffsetPeriod, dragonTrendPeriod, dragonTrendSmoothingEnabled, dragonTrendSmoothingPeriod, solarWaveATRPeriod, solarWaveTrendMultiplier, solarWaveStopMultiplier, enableSoundAlert, enableIndicatorCSVLog, logBarDetails);
		}

		public ActiveNikiMonitor ActiveNikiMonitor(ISeries<double> input, int minConfluenceRequired, int maxBarsAfterYellowSquare, int minSolarWaveCount, int cooldownBars, bool useTimeBasedCooldown, int cooldownSeconds, bool useRubyRiver, bool useDragonTrend, bool useSolarWave, bool useVIDYAPro, bool useEasyTrend, bool useT3Pro, bool useAAATrendSync, bool useAIQSuperBands, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, bool t3ProChaosSmoothingEnabled, int t3ProChaosSmoothingPeriod, bool t3ProFilterEnabled, double t3ProFilterMultiplier, int vIDYAPeriod, int vIDYAVolatilityPeriod, bool vIDYASmoothingEnabled, int vIDYASmoothingPeriod, bool vIDYAFilterEnabled, double vIDYAFilterMultiplier, int easyTrendPeriod, bool easyTrendSmoothingEnabled, int easyTrendSmoothingPeriod, bool easyTrendFilterEnabled, double easyTrendFilterMultiplier, int easyTrendATRPeriod, int rubyRiverMAPeriod, bool rubyRiverSmoothingEnabled, int rubyRiverSmoothingPeriod, double rubyRiverOffsetMultiplier, int rubyRiverOffsetPeriod, int dragonTrendPeriod, bool dragonTrendSmoothingEnabled, int dragonTrendSmoothingPeriod, int solarWaveATRPeriod, double solarWaveTrendMultiplier, double solarWaveStopMultiplier, bool enableSoundAlert, bool enableIndicatorCSVLog, bool logBarDetails)
		{
			if (cacheActiveNikiMonitor != null)
				for (int idx = 0; idx < cacheActiveNikiMonitor.Length; idx++)
					if (cacheActiveNikiMonitor[idx] != null && cacheActiveNikiMonitor[idx].MinConfluenceRequired == minConfluenceRequired && cacheActiveNikiMonitor[idx].MaxBarsAfterYellowSquare == maxBarsAfterYellowSquare && cacheActiveNikiMonitor[idx].MinSolarWaveCount == minSolarWaveCount && cacheActiveNikiMonitor[idx].CooldownBars == cooldownBars && cacheActiveNikiMonitor[idx].UseTimeBasedCooldown == useTimeBasedCooldown && cacheActiveNikiMonitor[idx].CooldownSeconds == cooldownSeconds && cacheActiveNikiMonitor[idx].UseRubyRiver == useRubyRiver && cacheActiveNikiMonitor[idx].UseDragonTrend == useDragonTrend && cacheActiveNikiMonitor[idx].UseSolarWave == useSolarWave && cacheActiveNikiMonitor[idx].UseVIDYAPro == useVIDYAPro && cacheActiveNikiMonitor[idx].UseEasyTrend == useEasyTrend && cacheActiveNikiMonitor[idx].UseT3Pro == useT3Pro && cacheActiveNikiMonitor[idx].UseAAATrendSync == useAAATrendSync && cacheActiveNikiMonitor[idx].UseAIQSuperBands == useAIQSuperBands && cacheActiveNikiMonitor[idx].T3ProPeriod == t3ProPeriod && cacheActiveNikiMonitor[idx].T3ProTCount == t3ProTCount && cacheActiveNikiMonitor[idx].T3ProVFactor == t3ProVFactor && cacheActiveNikiMonitor[idx].T3ProChaosSmoothingEnabled == t3ProChaosSmoothingEnabled && cacheActiveNikiMonitor[idx].T3ProChaosSmoothingPeriod == t3ProChaosSmoothingPeriod && cacheActiveNikiMonitor[idx].T3ProFilterEnabled == t3ProFilterEnabled && cacheActiveNikiMonitor[idx].T3ProFilterMultiplier == t3ProFilterMultiplier && cacheActiveNikiMonitor[idx].VIDYAPeriod == vIDYAPeriod && cacheActiveNikiMonitor[idx].VIDYAVolatilityPeriod == vIDYAVolatilityPeriod && cacheActiveNikiMonitor[idx].VIDYASmoothingEnabled == vIDYASmoothingEnabled && cacheActiveNikiMonitor[idx].VIDYASmoothingPeriod == vIDYASmoothingPeriod && cacheActiveNikiMonitor[idx].VIDYAFilterEnabled == vIDYAFilterEnabled && cacheActiveNikiMonitor[idx].VIDYAFilterMultiplier == vIDYAFilterMultiplier && cacheActiveNikiMonitor[idx].EasyTrendPeriod == easyTrendPeriod && cacheActiveNikiMonitor[idx].EasyTrendSmoothingEnabled == easyTrendSmoothingEnabled && cacheActiveNikiMonitor[idx].EasyTrendSmoothingPeriod == easyTrendSmoothingPeriod && cacheActiveNikiMonitor[idx].EasyTrendFilterEnabled == easyTrendFilterEnabled && cacheActiveNikiMonitor[idx].EasyTrendFilterMultiplier == easyTrendFilterMultiplier && cacheActiveNikiMonitor[idx].EasyTrendATRPeriod == easyTrendATRPeriod && cacheActiveNikiMonitor[idx].RubyRiverMAPeriod == rubyRiverMAPeriod && cacheActiveNikiMonitor[idx].RubyRiverSmoothingEnabled == rubyRiverSmoothingEnabled && cacheActiveNikiMonitor[idx].RubyRiverSmoothingPeriod == rubyRiverSmoothingPeriod && cacheActiveNikiMonitor[idx].RubyRiverOffsetMultiplier == rubyRiverOffsetMultiplier && cacheActiveNikiMonitor[idx].RubyRiverOffsetPeriod == rubyRiverOffsetPeriod && cacheActiveNikiMonitor[idx].DragonTrendPeriod == dragonTrendPeriod && cacheActiveNikiMonitor[idx].DragonTrendSmoothingEnabled == dragonTrendSmoothingEnabled && cacheActiveNikiMonitor[idx].DragonTrendSmoothingPeriod == dragonTrendSmoothingPeriod && cacheActiveNikiMonitor[idx].SolarWaveATRPeriod == solarWaveATRPeriod && cacheActiveNikiMonitor[idx].SolarWaveTrendMultiplier == solarWaveTrendMultiplier && cacheActiveNikiMonitor[idx].SolarWaveStopMultiplier == solarWaveStopMultiplier && cacheActiveNikiMonitor[idx].EnableSoundAlert == enableSoundAlert && cacheActiveNikiMonitor[idx].EnableIndicatorCSVLog == enableIndicatorCSVLog && cacheActiveNikiMonitor[idx].LogBarDetails == logBarDetails && cacheActiveNikiMonitor[idx].EqualsInput(input))
						return cacheActiveNikiMonitor[idx];
			return CacheIndicator<ActiveNikiMonitor>(new ActiveNikiMonitor(){ MinConfluenceRequired = minConfluenceRequired, MaxBarsAfterYellowSquare = maxBarsAfterYellowSquare, MinSolarWaveCount = minSolarWaveCount, CooldownBars = cooldownBars, UseTimeBasedCooldown = useTimeBasedCooldown, CooldownSeconds = cooldownSeconds, UseRubyRiver = useRubyRiver, UseDragonTrend = useDragonTrend, UseSolarWave = useSolarWave, UseVIDYAPro = useVIDYAPro, UseEasyTrend = useEasyTrend, UseT3Pro = useT3Pro, UseAAATrendSync = useAAATrendSync, UseAIQSuperBands = useAIQSuperBands, T3ProPeriod = t3ProPeriod, T3ProTCount = t3ProTCount, T3ProVFactor = t3ProVFactor, T3ProChaosSmoothingEnabled = t3ProChaosSmoothingEnabled, T3ProChaosSmoothingPeriod = t3ProChaosSmoothingPeriod, T3ProFilterEnabled = t3ProFilterEnabled, T3ProFilterMultiplier = t3ProFilterMultiplier, VIDYAPeriod = vIDYAPeriod, VIDYAVolatilityPeriod = vIDYAVolatilityPeriod, VIDYASmoothingEnabled = vIDYASmoothingEnabled, VIDYASmoothingPeriod = vIDYASmoothingPeriod, VIDYAFilterEnabled = vIDYAFilterEnabled, VIDYAFilterMultiplier = vIDYAFilterMultiplier, EasyTrendPeriod = easyTrendPeriod, EasyTrendSmoothingEnabled = easyTrendSmoothingEnabled, EasyTrendSmoothingPeriod = easyTrendSmoothingPeriod, EasyTrendFilterEnabled = easyTrendFilterEnabled, EasyTrendFilterMultiplier = easyTrendFilterMultiplier, EasyTrendATRPeriod = easyTrendATRPeriod, RubyRiverMAPeriod = rubyRiverMAPeriod, RubyRiverSmoothingEnabled = rubyRiverSmoothingEnabled, RubyRiverSmoothingPeriod = rubyRiverSmoothingPeriod, RubyRiverOffsetMultiplier = rubyRiverOffsetMultiplier, RubyRiverOffsetPeriod = rubyRiverOffsetPeriod, DragonTrendPeriod = dragonTrendPeriod, DragonTrendSmoothingEnabled = dragonTrendSmoothingEnabled, DragonTrendSmoothingPeriod = dragonTrendSmoothingPeriod, SolarWaveATRPeriod = solarWaveATRPeriod, SolarWaveTrendMultiplier = solarWaveTrendMultiplier, SolarWaveStopMultiplier = solarWaveStopMultiplier, EnableSoundAlert = enableSoundAlert, EnableIndicatorCSVLog = enableIndicatorCSVLog, LogBarDetails = logBarDetails }, input, ref cacheActiveNikiMonitor);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ActiveNikiMonitor ActiveNikiMonitor(int minConfluenceRequired, int maxBarsAfterYellowSquare, int minSolarWaveCount, int cooldownBars, bool useTimeBasedCooldown, int cooldownSeconds, bool useRubyRiver, bool useDragonTrend, bool useSolarWave, bool useVIDYAPro, bool useEasyTrend, bool useT3Pro, bool useAAATrendSync, bool useAIQSuperBands, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, bool t3ProChaosSmoothingEnabled, int t3ProChaosSmoothingPeriod, bool t3ProFilterEnabled, double t3ProFilterMultiplier, int vIDYAPeriod, int vIDYAVolatilityPeriod, bool vIDYASmoothingEnabled, int vIDYASmoothingPeriod, bool vIDYAFilterEnabled, double vIDYAFilterMultiplier, int easyTrendPeriod, bool easyTrendSmoothingEnabled, int easyTrendSmoothingPeriod, bool easyTrendFilterEnabled, double easyTrendFilterMultiplier, int easyTrendATRPeriod, int rubyRiverMAPeriod, bool rubyRiverSmoothingEnabled, int rubyRiverSmoothingPeriod, double rubyRiverOffsetMultiplier, int rubyRiverOffsetPeriod, int dragonTrendPeriod, bool dragonTrendSmoothingEnabled, int dragonTrendSmoothingPeriod, int solarWaveATRPeriod, double solarWaveTrendMultiplier, double solarWaveStopMultiplier, bool enableSoundAlert, bool enableIndicatorCSVLog, bool logBarDetails)
		{
			return indicator.ActiveNikiMonitor(Input, minConfluenceRequired, maxBarsAfterYellowSquare, minSolarWaveCount, cooldownBars, useTimeBasedCooldown, cooldownSeconds, useRubyRiver, useDragonTrend, useSolarWave, useVIDYAPro, useEasyTrend, useT3Pro, useAAATrendSync, useAIQSuperBands, t3ProPeriod, t3ProTCount, t3ProVFactor, t3ProChaosSmoothingEnabled, t3ProChaosSmoothingPeriod, t3ProFilterEnabled, t3ProFilterMultiplier, vIDYAPeriod, vIDYAVolatilityPeriod, vIDYASmoothingEnabled, vIDYASmoothingPeriod, vIDYAFilterEnabled, vIDYAFilterMultiplier, easyTrendPeriod, easyTrendSmoothingEnabled, easyTrendSmoothingPeriod, easyTrendFilterEnabled, easyTrendFilterMultiplier, easyTrendATRPeriod, rubyRiverMAPeriod, rubyRiverSmoothingEnabled, rubyRiverSmoothingPeriod, rubyRiverOffsetMultiplier, rubyRiverOffsetPeriod, dragonTrendPeriod, dragonTrendSmoothingEnabled, dragonTrendSmoothingPeriod, solarWaveATRPeriod, solarWaveTrendMultiplier, solarWaveStopMultiplier, enableSoundAlert, enableIndicatorCSVLog, logBarDetails);
		}

		public Indicators.ActiveNikiMonitor ActiveNikiMonitor(ISeries<double> input , int minConfluenceRequired, int maxBarsAfterYellowSquare, int minSolarWaveCount, int cooldownBars, bool useTimeBasedCooldown, int cooldownSeconds, bool useRubyRiver, bool useDragonTrend, bool useSolarWave, bool useVIDYAPro, bool useEasyTrend, bool useT3Pro, bool useAAATrendSync, bool useAIQSuperBands, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, bool t3ProChaosSmoothingEnabled, int t3ProChaosSmoothingPeriod, bool t3ProFilterEnabled, double t3ProFilterMultiplier, int vIDYAPeriod, int vIDYAVolatilityPeriod, bool vIDYASmoothingEnabled, int vIDYASmoothingPeriod, bool vIDYAFilterEnabled, double vIDYAFilterMultiplier, int easyTrendPeriod, bool easyTrendSmoothingEnabled, int easyTrendSmoothingPeriod, bool easyTrendFilterEnabled, double easyTrendFilterMultiplier, int easyTrendATRPeriod, int rubyRiverMAPeriod, bool rubyRiverSmoothingEnabled, int rubyRiverSmoothingPeriod, double rubyRiverOffsetMultiplier, int rubyRiverOffsetPeriod, int dragonTrendPeriod, bool dragonTrendSmoothingEnabled, int dragonTrendSmoothingPeriod, int solarWaveATRPeriod, double solarWaveTrendMultiplier, double solarWaveStopMultiplier, bool enableSoundAlert, bool enableIndicatorCSVLog, bool logBarDetails)
		{
			return indicator.ActiveNikiMonitor(input, minConfluenceRequired, maxBarsAfterYellowSquare, minSolarWaveCount, cooldownBars, useTimeBasedCooldown, cooldownSeconds, useRubyRiver, useDragonTrend, useSolarWave, useVIDYAPro, useEasyTrend, useT3Pro, useAAATrendSync, useAIQSuperBands, t3ProPeriod, t3ProTCount, t3ProVFactor, t3ProChaosSmoothingEnabled, t3ProChaosSmoothingPeriod, t3ProFilterEnabled, t3ProFilterMultiplier, vIDYAPeriod, vIDYAVolatilityPeriod, vIDYASmoothingEnabled, vIDYASmoothingPeriod, vIDYAFilterEnabled, vIDYAFilterMultiplier, easyTrendPeriod, easyTrendSmoothingEnabled, easyTrendSmoothingPeriod, easyTrendFilterEnabled, easyTrendFilterMultiplier, easyTrendATRPeriod, rubyRiverMAPeriod, rubyRiverSmoothingEnabled, rubyRiverSmoothingPeriod, rubyRiverOffsetMultiplier, rubyRiverOffsetPeriod, dragonTrendPeriod, dragonTrendSmoothingEnabled, dragonTrendSmoothingPeriod, solarWaveATRPeriod, solarWaveTrendMultiplier, solarWaveStopMultiplier, enableSoundAlert, enableIndicatorCSVLog, logBarDetails);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ActiveNikiMonitor ActiveNikiMonitor(int minConfluenceRequired, int maxBarsAfterYellowSquare, int minSolarWaveCount, int cooldownBars, bool useTimeBasedCooldown, int cooldownSeconds, bool useRubyRiver, bool useDragonTrend, bool useSolarWave, bool useVIDYAPro, bool useEasyTrend, bool useT3Pro, bool useAAATrendSync, bool useAIQSuperBands, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, bool t3ProChaosSmoothingEnabled, int t3ProChaosSmoothingPeriod, bool t3ProFilterEnabled, double t3ProFilterMultiplier, int vIDYAPeriod, int vIDYAVolatilityPeriod, bool vIDYASmoothingEnabled, int vIDYASmoothingPeriod, bool vIDYAFilterEnabled, double vIDYAFilterMultiplier, int easyTrendPeriod, bool easyTrendSmoothingEnabled, int easyTrendSmoothingPeriod, bool easyTrendFilterEnabled, double easyTrendFilterMultiplier, int easyTrendATRPeriod, int rubyRiverMAPeriod, bool rubyRiverSmoothingEnabled, int rubyRiverSmoothingPeriod, double rubyRiverOffsetMultiplier, int rubyRiverOffsetPeriod, int dragonTrendPeriod, bool dragonTrendSmoothingEnabled, int dragonTrendSmoothingPeriod, int solarWaveATRPeriod, double solarWaveTrendMultiplier, double solarWaveStopMultiplier, bool enableSoundAlert, bool enableIndicatorCSVLog, bool logBarDetails)
		{
			return indicator.ActiveNikiMonitor(Input, minConfluenceRequired, maxBarsAfterYellowSquare, minSolarWaveCount, cooldownBars, useTimeBasedCooldown, cooldownSeconds, useRubyRiver, useDragonTrend, useSolarWave, useVIDYAPro, useEasyTrend, useT3Pro, useAAATrendSync, useAIQSuperBands, t3ProPeriod, t3ProTCount, t3ProVFactor, t3ProChaosSmoothingEnabled, t3ProChaosSmoothingPeriod, t3ProFilterEnabled, t3ProFilterMultiplier, vIDYAPeriod, vIDYAVolatilityPeriod, vIDYASmoothingEnabled, vIDYASmoothingPeriod, vIDYAFilterEnabled, vIDYAFilterMultiplier, easyTrendPeriod, easyTrendSmoothingEnabled, easyTrendSmoothingPeriod, easyTrendFilterEnabled, easyTrendFilterMultiplier, easyTrendATRPeriod, rubyRiverMAPeriod, rubyRiverSmoothingEnabled, rubyRiverSmoothingPeriod, rubyRiverOffsetMultiplier, rubyRiverOffsetPeriod, dragonTrendPeriod, dragonTrendSmoothingEnabled, dragonTrendSmoothingPeriod, solarWaveATRPeriod, solarWaveTrendMultiplier, solarWaveStopMultiplier, enableSoundAlert, enableIndicatorCSVLog, logBarDetails);
		}

		public Indicators.ActiveNikiMonitor ActiveNikiMonitor(ISeries<double> input , int minConfluenceRequired, int maxBarsAfterYellowSquare, int minSolarWaveCount, int cooldownBars, bool useTimeBasedCooldown, int cooldownSeconds, bool useRubyRiver, bool useDragonTrend, bool useSolarWave, bool useVIDYAPro, bool useEasyTrend, bool useT3Pro, bool useAAATrendSync, bool useAIQSuperBands, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, bool t3ProChaosSmoothingEnabled, int t3ProChaosSmoothingPeriod, bool t3ProFilterEnabled, double t3ProFilterMultiplier, int vIDYAPeriod, int vIDYAVolatilityPeriod, bool vIDYASmoothingEnabled, int vIDYASmoothingPeriod, bool vIDYAFilterEnabled, double vIDYAFilterMultiplier, int easyTrendPeriod, bool easyTrendSmoothingEnabled, int easyTrendSmoothingPeriod, bool easyTrendFilterEnabled, double easyTrendFilterMultiplier, int easyTrendATRPeriod, int rubyRiverMAPeriod, bool rubyRiverSmoothingEnabled, int rubyRiverSmoothingPeriod, double rubyRiverOffsetMultiplier, int rubyRiverOffsetPeriod, int dragonTrendPeriod, bool dragonTrendSmoothingEnabled, int dragonTrendSmoothingPeriod, int solarWaveATRPeriod, double solarWaveTrendMultiplier, double solarWaveStopMultiplier, bool enableSoundAlert, bool enableIndicatorCSVLog, bool logBarDetails)
		{
			return indicator.ActiveNikiMonitor(input, minConfluenceRequired, maxBarsAfterYellowSquare, minSolarWaveCount, cooldownBars, useTimeBasedCooldown, cooldownSeconds, useRubyRiver, useDragonTrend, useSolarWave, useVIDYAPro, useEasyTrend, useT3Pro, useAAATrendSync, useAIQSuperBands, t3ProPeriod, t3ProTCount, t3ProVFactor, t3ProChaosSmoothingEnabled, t3ProChaosSmoothingPeriod, t3ProFilterEnabled, t3ProFilterMultiplier, vIDYAPeriod, vIDYAVolatilityPeriod, vIDYASmoothingEnabled, vIDYASmoothingPeriod, vIDYAFilterEnabled, vIDYAFilterMultiplier, easyTrendPeriod, easyTrendSmoothingEnabled, easyTrendSmoothingPeriod, easyTrendFilterEnabled, easyTrendFilterMultiplier, easyTrendATRPeriod, rubyRiverMAPeriod, rubyRiverSmoothingEnabled, rubyRiverSmoothingPeriod, rubyRiverOffsetMultiplier, rubyRiverOffsetPeriod, dragonTrendPeriod, dragonTrendSmoothingEnabled, dragonTrendSmoothingPeriod, solarWaveATRPeriod, solarWaveTrendMultiplier, solarWaveStopMultiplier, enableSoundAlert, enableIndicatorCSVLog, logBarDetails);
		}
	}
}

#endregion
