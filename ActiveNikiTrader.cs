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
using System.Windows.Media;
using System.Windows.Shapes;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// ActiveNikiTrader - AIQ_1 trigger + any indicator confirmation with 8-indicator confluence filter
    /// 
    /// Split into partial classes:
    ///   - ActiveNikiTrader.cs           (Main: fields, parameters, OnStateChange, OnBarUpdate)
    ///   - ActiveNikiTrader.Panel.cs     (Panel UI: create, update, event handlers)
    ///   - ActiveNikiTrader.Indicators.cs (Indicator loading, accessors, reflection)
    ///   - ActiveNikiTrader.Signals.cs   (Confluence, confirmation, trading hours)
    ///   - ActiveNikiTrader.Logging.cs   (CSV logging, signal logging, file management)
    /// </summary>
    public partial class ActiveNikiTrader : Strategy
    {
        #region Fields
        // ninZa indicator references (for VPS with licensed indicators)
        private object rubyRiver, vidyaPro, easyTrend, dragonTrend, solarWave, ninZaT3Pro, aaaTrendSync;
        private FieldInfo rrIsUptrend, vyIsUptrend, etIsUptrend, dtPrevSignal, swIsUptrend, swCountWave, t3pIsUptrend, aaaIsUptrend;
        
        // Native AIQ_1 indicator reference (from AIQ folder)
        private object nativeAiq1;
        private FieldInfo nativeAiq1TrendState;  // Int32: 1=up, -1=down
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
        
        // Equivalent indicators (hosted by strategy - fallback only)
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
        private int barsSinceYellowSquare = -1;  // -1 = no active LONG window, 0+ = counting bars since AIQ1 flipped UP
        private int barsSinceOrangeSquare = -1;  // -1 = no active SHORT window, 0+ = counting bars since AIQ1 flipped DN
        private int barsSinceLastSignal = -1;    // -1 = no cooldown active, 0+ = counting bars since last signal
        private DateTime lastSignalTime = DateTime.MinValue;  // For time-based cooldown
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
        private Point resizeStartPoint;
        private double resizeStartWidth, resizeStartHeight;
        private TranslateTransform panelTransform;
        private ScaleTransform panelScale;
        private string panelSettingsFile;
        private Border resizeGrip;
        private CheckBox chkRubyRiver, chkDragonTrend, chkSolarWave, chkVIDYA, chkEasyTrend, chkT3Pro, chkAAASync, chkSuperBands;
        private TextBlock lblRubyRiver, lblDragonTrend, lblSolarWave, lblVIDYA, lblEasyTrend, lblT3Pro, lblAAASync, lblSuperBands;
        private TextBlock lblAIQ1Status, lblWindowStatus;
        private TextBlock lblTradeStatus, lblSessionStats, lblTriggerMode, lblLastSignal, lblSubtitle;
        private Border signalBorder;
        
        // Session tracking
        private int signalCount;
        private string lastSignalText = "";
        private string logFilePath;
        private StreamWriter logWriter;
        private string chartSessionId;
        
        // CSV debug logging for indicator comparison
        private string csvLogFilePath;
        private StreamWriter csvWriter;
        
        // Daily P&L tracking
        private double dailyPnL = 0;
        private DateTime lastTradeDate = DateTime.MinValue;
        private bool dailyLossLimitHit = false;
        private bool dailyProfitTargetHit = false;
        private int dailyTradeCount = 0;
        
        // Dynamic exit tracking
        private bool dynamicExitActive = false;
        private double entryPrice = 0;
        private double trailStopPrice = 0;
        private ATR atrIndicator;
        
        #endregion
        
        #region Parameters
        [NinjaScriptProperty]
        [Range(2, 8)]
        [Display(Name="Min Confluence Required", Description="Minimum indicators agreeing (2-7)", Order=1, GroupName="1. Signal Filters")]
        public int MinConfluenceRequired { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 3)]
        [Display(Name="Max Bars After Yellow Square", Description="Bars after AIQ1 flip to confirm with any indicator (0-3)", Order=2, GroupName="1. Signal Filters")]
        public int MaxBarsAfterYellowSquare { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name="Min Solar Wave Count", Order=3, GroupName="1. Signal Filters")]
        public int MinSolarWaveCount { get; set; }
        
        [NinjaScriptProperty]
        [Range(10, 1000)]
        [Display(Name="Stop Loss USD", Description="Stop loss amount in dollars", Order=4, GroupName="1. Signal Filters")]
        public double StopLossUSD { get; set; }
        
        [NinjaScriptProperty]
        [Range(10, 1000)]
        [Display(Name="Take Profit USD", Description="Take profit amount in dollars", Order=5, GroupName="1. Signal Filters")]
        public double TakeProfitUSD { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Cooldown Bars", Description="Minimum bars between signals (0=disabled)", Order=6, GroupName="1. Signal Filters")]
        public int CooldownBars { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Enable Auto Trading", Description="Place orders automatically when signals fire", Order=7, GroupName="1. Signal Filters")]
        public bool EnableAutoTrading { get; set; }
        
        [NinjaScriptProperty]
        [Range(2, 8)]
        [Display(Name="Min Confluence For Auto Trade", Description="Higher confluence required to actually place orders (2-7)", Order=8, GroupName="1. Signal Filters")]
        public int MinConfluenceForAutoTrade { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Use Trading Hours Filter", Description="Only allow trades during specified hours", Order=9, GroupName="1. Signal Filters")]
        public bool UseTradingHoursFilter { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name="Session 1 Start Hour", Description="First session start hour (0-23)", Order=10, GroupName="1. Signal Filters")]
        public int Session1StartHour { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name="Session 1 Start Minute", Description="First session start minute (0-59)", Order=11, GroupName="1. Signal Filters")]
        public int Session1StartMinute { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name="Session 1 End Hour", Description="First session end hour (0-23)", Order=12, GroupName="1. Signal Filters")]
        public int Session1EndHour { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name="Session 1 End Minute", Description="First session end minute (0-59)", Order=13, GroupName="1. Signal Filters")]
        public int Session1EndMinute { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name="Session 2 Start Hour", Description="Second session start hour (0-23)", Order=14, GroupName="1. Signal Filters")]
        public int Session2StartHour { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name="Session 2 Start Minute", Description="Second session start minute (0-59)", Order=15, GroupName="1. Signal Filters")]
        public int Session2StartMinute { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name="Session 2 End Hour", Description="Second session end hour (0-23)", Order=16, GroupName="1. Signal Filters")]
        public int Session2EndHour { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name="Session 2 End Minute", Description="Second session end minute (0-59)", Order=17, GroupName="1. Signal Filters")]
        public int Session2EndMinute { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Close Before News", Description="Close positions before 8:30 AM news window", Order=18, GroupName="1. Signal Filters")]
        public bool CloseBeforeNews { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name="News Close Hour", Description="Hour to close before news (0-23)", Order=19, GroupName="1. Signal Filters")]
        public int NewsCloseHour { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name="News Close Minute", Description="Minute to close before news (0-59)", Order=20, GroupName="1. Signal Filters")]
        public int NewsCloseMinute { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Close At End Of Day", Description="Close all positions at end of trading day", Order=21, GroupName="1. Signal Filters")]
        public bool CloseAtEndOfDay { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name="EOD Close Hour", Description="Hour to close at end of day (0-23)", Order=22, GroupName="1. Signal Filters")]
        public int EODCloseHour { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name="EOD Close Minute", Description="Minute to close at end of day (0-59)", Order=23, GroupName="1. Signal Filters")]
        public int EODCloseMinute { get; set; }
        
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
        [Display(Name="UniRenko Mode", Description="Enable adjustments for UniRenko/range bars", Order=1, GroupName="10. UniRenko Settings")]
        public bool UniRenkoMode { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Use Time-Based Cooldown", Description="Use seconds instead of bars for cooldown (recommended for UniRenko)", Order=2, GroupName="10. UniRenko Settings")]
        public bool UseTimeBasedCooldown { get; set; }
        
        [NinjaScriptProperty]
        [Range(10, 600)]
        [Display(Name="Cooldown Seconds", Description="Seconds between signals when using time-based cooldown", Order=3, GroupName="10. UniRenko Settings")]
        public int CooldownSeconds { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Log Bar Details", Description="Log detailed bar info for debugging UniRenko behavior", Order=4, GroupName="10. UniRenko Settings")]
        public bool LogBarDetails { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Enable Daily Loss Limit", Description="Stop trading if daily loss exceeds limit", Order=1, GroupName="11. Risk Management")]
        public bool EnableDailyLossLimit { get; set; }
        
        [NinjaScriptProperty]
        [Range(50, 5000)]
        [Display(Name="Daily Loss Limit USD", Description="Maximum loss allowed per day before stopping", Order=2, GroupName="11. Risk Management")]
        public double DailyLossLimitUSD { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Reset Daily P&L at Session Start", Description="Reset daily P&L tracking at start of trading session", Order=3, GroupName="11. Risk Management")]
        public bool ResetDailyPnLAtSessionStart { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Enable Daily Profit Target", Description="Stop trading if daily profit exceeds target", Order=4, GroupName="11. Risk Management")]
        public bool EnableDailyProfitTarget { get; set; }
        
        [NinjaScriptProperty]
        [Range(100, 10000)]
        [Display(Name="Daily Profit Target USD", Description="Stop trading after reaching this profit", Order=5, GroupName="11. Risk Management")]
        public double DailyProfitTargetUSD { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name="Stop Loss Buffer Ticks", Description="Extra ticks added to stop loss to reduce slippage (0=disabled)", Order=6, GroupName="11. Risk Management")]
        public int StopLossBufferTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Enable Dynamic Exit", Description="Let profits run when trend continues beyond TP", Order=1, GroupName="12. Dynamic Exit")]
        public bool EnableDynamicExit { get; set; }
        
        [NinjaScriptProperty]
        [Range(2, 8)]
        [Display(Name="Min Confluence To Stay", Description="Minimum confluence to keep position open past TP (2-7)", Order=2, GroupName="12. Dynamic Exit")]
        public int MinConfluenceToStay { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.25, 5.0)]
        [Display(Name="Trail Stop ATR Multiplier", Description="Trail stop distance in ATR multiples once past TP", Order=3, GroupName="12. Dynamic Exit")]
        public double TrailStopATRMultiplier { get; set; }
        
        [NinjaScriptProperty]
        [Range(10, 1000)]
        [Display(Name="Max Profit USD", Description="Maximum profit to ride before forcing exit", Order=4, GroupName="12. Dynamic Exit")]
        public double MaxProfitUSD { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Enable Indicator CSV Log", Description="Log raw indicator values to CSV for comparison/tuning", Order=1, GroupName="13. Debug")]
        public bool EnableIndicatorCSVLog { get; set; }
        #endregion
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "ActiveNikiTrader";
                Description = "AIQ_1 trigger + any indicator confirmation with 8-indicator confluence filter (LONG + SHORT)";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                
                // Signal filters
                MinConfluenceRequired = 5;
                MaxBarsAfterYellowSquare = 3;
                MinSolarWaveCount = 1;
                StopLossUSD = 80;
                TakeProfitUSD = 200;
                CooldownBars = 10;
                EnableAutoTrading = false;
                MinConfluenceForAutoTrade = 5;
                
                // Trading hours filter - Optimized based on backtest analysis (10:00-11:00 only)
                // Data: 182 trades, 38% win rate, +1,666t total, +9.2t per trade
                UseTradingHoursFilter = true;
                Session1StartHour = 10;
                Session1StartMinute = 0;
                Session1EndHour = 10;
                Session1EndMinute = 59;
                Session2StartHour = 10;
                Session2StartMinute = 0;
                Session2EndHour = 10;
                Session2EndMinute = 59;
                
                // Auto-close positions
                CloseBeforeNews = true;
                NewsCloseHour = 8;
                NewsCloseMinute = 28;
                CloseAtEndOfDay = true;
                EODCloseHour = 15;
                EODCloseMinute = 58;
                
                // Indicator selection - all 7 enabled by default
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
                
                // UniRenko settings
                UniRenkoMode = false;
                UseTimeBasedCooldown = false;
                CooldownSeconds = 120;  // 2 minutes default
                LogBarDetails = false;
                
                // Risk Management
                EnableDailyLossLimit = true;
                DailyLossLimitUSD = 300;
                EnableDailyProfitTarget = true;
                DailyProfitTargetUSD = 600;
                ResetDailyPnLAtSessionStart = true;
                StopLossBufferTicks = 2;  // Add 2 ticks buffer to reduce slippage
                
                // Dynamic Exit - let profits run when trend continues
                EnableDynamicExit = true;
                MinConfluenceToStay = 4;
                TrailStopATRMultiplier = 1.5;
                MaxProfitUSD = 500;  // Force exit at $500 profit
                
                // Debug - CSV indicator logging OFF by default
                EnableIndicatorCSVLog = false;
            }
            else if (State == State.DataLoaded)
            {
                chartSessionId = DateTime.Now.ToString("HHmmss") + "_" + new Random().Next(1000, 9999);
                InitializeLogFile();
                InitializeCSVLog();
                
                // Initialize all equivalent indicators (hosted by strategy)
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
                
                // Initialize ATR for dynamic exit trailing stop
                atrIndicator = ATR(14);
                
                LogAlways($"ActiveNikiTrader | 7-indicator confluence | SignalΓëÑ{MinConfluenceRequired} TradeΓëÑ{MinConfluenceForAutoTrade} | CD={CooldownBars} | SL=${StopLossUSD} TP=${TakeProfitUSD} | AutoTrade={EnableAutoTrading}");
                if (StopLossBufferTicks > 0)
                    LogAlways($"SL Buffer: {StopLossBufferTicks} ticks");
                if (EnableDynamicExit)
                    LogAlways($"🚀 Dynamic Exit: ON | MinConf={MinConfluenceToStay} | Trail={TrailStopATRMultiplier}xATR | MaxProfit=${MaxProfitUSD}");
                if (UniRenkoMode)
                {
                    LogAlways($"*** UNIRENKO MODE ENABLED ***");
                    if (UseTimeBasedCooldown)
                        LogAlways($"Time-Based Cooldown: {CooldownSeconds} seconds");
                    if (LogBarDetails)
                        LogAlways($"Bar Detail Logging: ON");
                }
                if (UseTradingHoursFilter)
                    LogAlways($"Trading Hours: {GetTradingHoursString()}");
                if (CloseBeforeNews)
                    LogAlways($"Auto-Close Before News: {NewsCloseHour:D2}:{NewsCloseMinute:D2}");
                if (CloseAtEndOfDay)
                    LogAlways($"Auto-Close EOD: {EODCloseHour:D2}:{EODCloseMinute:D2}");
                if (EnableDailyLossLimit)
                    LogAlways($"🛡️ Daily Loss Limit: ${DailyLossLimitUSD:F0}");
                if (EnableDailyProfitTarget)
                    LogAlways($"🎯 Daily Profit Target: ${DailyProfitTargetUSD:F0}");
                if (EnableIndicatorCSVLog)
                    LogAlways($"📊 CSV Indicator Log: ENABLED");
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
        
        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade || !indicatorsReady) return;
            
            DateTime barTime = Time[0];
            
            // Write CSV log row (every bar if enabled)
            WriteCSVRow(barTime);
            
            // Daily P&L reset check - reset at start of new trading day
            if (ResetDailyPnLAtSessionStart && barTime.Date != lastTradeDate.Date)
            {
                if (dailyPnL != 0 || dailyTradeCount > 0)
                    PrintAndLog($"📊 NEW DAY: Resetting Daily P&L (was ${dailyPnL:F2}, {dailyTradeCount} trades)");
                dailyPnL = 0;
                dailyTradeCount = 0;
                dailyLossLimitHit = false;
                dailyProfitTargetHit = false;
                lastTradeDate = barTime.Date;
            }
            
            // Daily loss limit / profit target check
            if ((EnableDailyLossLimit && dailyLossLimitHit) || (EnableDailyProfitTarget && dailyProfitTargetHit))
            {
                return;
            }
            
            // UniRenko debug logging
            if (LogBarDetails && UniRenkoMode)
            {
                var (bull, bear, total) = GetConfluence();
                PrintAndLog($"[BAR {CurrentBar}] {barTime:HH:mm:ss} | O={Open[0]:F2} H={High[0]:F2} L={Low[0]:F2} C={Close[0]:F2} | AIQ1={Ts(AIQ1_IsUp)} RR={Ts(RR_IsUp)} Bull={bull} Bear={bear}");
            }
            
            if (CloseBeforeNews && Position.MarketPosition != MarketPosition.Flat)
            {
                int currentMinutes = barTime.Hour * 60 + barTime.Minute;
                int newsCloseMinutes = NewsCloseHour * 60 + NewsCloseMinute;
                
                if (currentMinutes >= newsCloseMinutes && currentMinutes < newsCloseMinutes + 32)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        ExitLong("Long", "PreNews Exit");
                        PrintAndLog($">>> AUTO-CLOSE LONG @ {barTime:HH:mm:ss} - Before news window");
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        ExitShort("Short", "PreNews Exit");
                        PrintAndLog($">>> AUTO-CLOSE SHORT @ {barTime:HH:mm:ss} - Before news window");
                    }
                }
            }
            
            if (CloseAtEndOfDay && Position.MarketPosition != MarketPosition.Flat)
            {
                int currentMinutes = barTime.Hour * 60 + barTime.Minute;
                int eodCloseMinutes = EODCloseHour * 60 + EODCloseMinute;
                
                if (currentMinutes >= eodCloseMinutes)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        ExitLong("Long", "EOD Exit");
                        PrintAndLog($">>> AUTO-CLOSE LONG @ {barTime:HH:mm:ss} - End of day");
                    }
                    else if (Position.MarketPosition == MarketPosition.Short)
                    {
                        ExitShort("Short", "EOD Exit");
                        PrintAndLog($">>> AUTO-CLOSE SHORT @ {barTime:HH:mm:ss} - End of day");
                    }
                }
            }
            
            // DYNAMIC EXIT MANAGEMENT
            if (EnableDynamicExit && Position.MarketPosition != MarketPosition.Flat && entryPrice > 0)
            {
                double currentPrice = Close[0];
                double pointValue = Instrument.MasterInstrument.PointValue;
                double tpPoints = pointValue > 0 ? TakeProfitUSD / pointValue : 10;
                double maxProfitPoints = pointValue > 0 ? MaxProfitUSD / pointValue : 25;
                double atrValue = atrIndicator[0];
                
                var (bull, bear, total) = GetConfluence();
                bool isLong = Position.MarketPosition == MarketPosition.Long;
                
                double unrealizedPoints = isLong ? (currentPrice - entryPrice) : (entryPrice - currentPrice);
                double unrealizedPnL = unrealizedPoints * pointValue;
                
                bool pastTpLevel = unrealizedPoints >= tpPoints;
                
                if (unrealizedPoints >= maxProfitPoints)
                {
                    if (isLong)
                    {
                        ExitLong("Long", "MaxProfit Exit");
                        PrintAndLog($"🎯 DYNAMIC EXIT LONG @ {barTime:HH:mm:ss} | MAX PROFIT HIT ${unrealizedPnL:F2}");
                    }
                    else
                    {
                        ExitShort("Short", "MaxProfit Exit");
                        PrintAndLog($"🎯 DYNAMIC EXIT SHORT @ {barTime:HH:mm:ss} | MAX PROFIT HIT ${unrealizedPnL:F2}");
                    }
                    entryPrice = 0;
                    dynamicExitActive = false;
                }
                else if (pastTpLevel)
                {
                    bool confluenceConfirmsTrend = isLong ? (bull >= MinConfluenceToStay) : (bear >= MinConfluenceToStay);
                    bool trendStillValid = isLong ? RR_IsUp : !RR_IsUp;
                    
                    if (!dynamicExitActive)
                    {
                        if (confluenceConfirmsTrend && trendStillValid)
                        {
                            dynamicExitActive = true;
                            double trailDistance = atrValue * TrailStopATRMultiplier;
                            trailStopPrice = isLong ? (currentPrice - trailDistance) : (currentPrice + trailDistance);
                            
                            if (isLong)
                                SetStopLoss("Long", CalculationMode.Price, trailStopPrice, true);
                            else
                                SetStopLoss("Short", CalculationMode.Price, trailStopPrice, true);
                                
                            PrintAndLog($"🚀 DYNAMIC MODE ACTIVATED @ {barTime:HH:mm:ss} | P&L=${unrealizedPnL:F2} | Trail={trailStopPrice:F2} | Conf={bull}/{bear}");
                        }
                        else
                        {
                            if (isLong)
                            {
                                ExitLong("Long", "DynamicTP Exit");
                                PrintAndLog($"🎯 DYNAMIC EXIT LONG @ {barTime:HH:mm:ss} | Conf dropped (Bull:{bull}<{MinConfluenceToStay}) | P&L=${unrealizedPnL:F2}");
                            }
                            else
                            {
                                ExitShort("Short", "DynamicTP Exit");
                                PrintAndLog($"🎯 DYNAMIC EXIT SHORT @ {barTime:HH:mm:ss} | Conf dropped (Bear:{bear}<{MinConfluenceToStay}) | P&L=${unrealizedPnL:F2}");
                            }
                            entryPrice = 0;
                        }
                    }
                    else
                    {
                        double trailDistance = atrValue * TrailStopATRMultiplier;
                        
                        if (isLong)
                        {
                            double newTrailStop = currentPrice - trailDistance;
                            if (newTrailStop > trailStopPrice)
                            {
                                trailStopPrice = newTrailStop;
                                SetStopLoss("Long", CalculationMode.Price, trailStopPrice, true);
                                PrintAndLog($"📈 TRAIL STOP UPDATED @ {barTime:HH:mm:ss} | New Stop={trailStopPrice:F2} | P&L=${unrealizedPnL:F2}");
                            }
                            
                            if (currentPrice <= trailStopPrice || !trendStillValid || !confluenceConfirmsTrend)
                            {
                                ExitLong("Long", "DynamicTrail Exit");
                                string reason = currentPrice <= trailStopPrice ? "Trail Stop Hit" : 
                                                !trendStillValid ? "RR Flipped" : $"Conf={bull}<{MinConfluenceToStay}";
                                PrintAndLog($"🎯 DYNAMIC EXIT LONG @ {barTime:HH:mm:ss} | {reason} | P&L=${unrealizedPnL:F2}");
                                entryPrice = 0;
                                dynamicExitActive = false;
                            }
                        }
                        else
                        {
                            double newTrailStop = currentPrice + trailDistance;
                            if (newTrailStop < trailStopPrice)
                            {
                                trailStopPrice = newTrailStop;
                                SetStopLoss("Short", CalculationMode.Price, trailStopPrice, true);
                                PrintAndLog($"📉 TRAIL STOP UPDATED @ {barTime:HH:mm:ss} | New Stop={trailStopPrice:F2} | P&L=${unrealizedPnL:F2}");
                            }
                            
                            if (currentPrice >= trailStopPrice || !trendStillValid || !confluenceConfirmsTrend)
                            {
                                ExitShort("Short", "DynamicTrail Exit");
                                string reason = currentPrice >= trailStopPrice ? "Trail Stop Hit" : 
                                                !trendStillValid ? "RR Flipped" : $"Conf={bear}<{MinConfluenceToStay}";
                                PrintAndLog($"🎯 DYNAMIC EXIT SHORT @ {barTime:HH:mm:ss} | {reason} | P&L=${unrealizedPnL:F2}");
                                entryPrice = 0;
                                dynamicExitActive = false;
                            }
                        }
                    }
                }
            }
            
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                entryPrice = 0;
                dynamicExitActive = false;
                trailStopPrice = 0;
            }
            
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
                return;
            }
            
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
                        
                        if (EnableAutoTrading && Position.MarketPosition == MarketPosition.Flat)
                        {
                            if (bull >= MinConfluenceForAutoTrade)
                            {
                                if (IsTradingHoursAllowed(barTime))
                                {
                                    double stopPoints = Instrument.MasterInstrument.PointValue > 0 ? StopLossUSD / Instrument.MasterInstrument.PointValue : 5;
                                    double tpPoints = Instrument.MasterInstrument.PointValue > 0 ? TakeProfitUSD / Instrument.MasterInstrument.PointValue : 3;
                                    
                                    double slTicks = (stopPoints / TickSize) + StopLossBufferTicks;
                                    
                                    if (EnableDynamicExit)
                                    {
                                        SetStopLoss("Long", CalculationMode.Ticks, slTicks, true);
                                        entryPrice = GetCurrentAsk();
                                        dynamicExitActive = false;
                                        trailStopPrice = 0;
                                    }
                                    else
                                    {
                                        SetStopLoss("Long", CalculationMode.Ticks, slTicks, true);
                                        SetProfitTarget("Long", CalculationMode.Ticks, tpPoints / TickSize);
                                    }
                                    EnterLong("Long");
                                    PrintAndLog($">>> ORDER PLACED: LONG @ Market | SL={stopPoints:F2}pts (+{StopLossBufferTicks}t buffer) TP={tpPoints:F2}pts{(EnableDynamicExit ? " [DYNAMIC]" : "")}");
                                }
                                else
                                {
                                    PrintAndLog($">>> OUTSIDE TRADING HOURS: LONG signal not traded @ {barTime:HH:mm:ss}");
                                }
                            }
                            else
                            {
                                PrintAndLog($">>> SIGNAL ONLY (no trade): Confluence {bull}/{total} < AutoTrade threshold {MinConfluenceForAutoTrade}");
                            }
                        }
                        
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
                        
                        if (EnableAutoTrading && Position.MarketPosition == MarketPosition.Flat)
                        {
                            if (bear >= MinConfluenceForAutoTrade)
                            {
                                if (IsTradingHoursAllowed(barTime))
                                {
                                    double stopPoints = Instrument.MasterInstrument.PointValue > 0 ? StopLossUSD / Instrument.MasterInstrument.PointValue : 5;
                                    double tpPoints = Instrument.MasterInstrument.PointValue > 0 ? TakeProfitUSD / Instrument.MasterInstrument.PointValue : 3;
                                    
                                    double slTicks = (stopPoints / TickSize) + StopLossBufferTicks;
                                    
                                    if (EnableDynamicExit)
                                    {
                                        SetStopLoss("Short", CalculationMode.Ticks, slTicks, true);
                                        entryPrice = GetCurrentBid();
                                        dynamicExitActive = false;
                                        trailStopPrice = 0;
                                    }
                                    else
                                    {
                                        SetStopLoss("Short", CalculationMode.Ticks, slTicks, true);
                                        SetProfitTarget("Short", CalculationMode.Ticks, tpPoints / TickSize);
                                    }
                                    EnterShort("Short");
                                    PrintAndLog($">>> ORDER PLACED: SHORT @ Market | SL={stopPoints:F2}pts (+{StopLossBufferTicks}t buffer) TP={tpPoints:F2}pts{(EnableDynamicExit ? " [DYNAMIC]" : "")}");
                                }
                                else
                                {
                                    PrintAndLog($">>> OUTSIDE TRADING HOURS: SHORT signal not traded @ {barTime:HH:mm:ss}");
                                }
                            }
                            else
                            {
                                PrintAndLog($">>> SIGNAL ONLY (no trade): Confluence {bear}/{total} < AutoTrade threshold {MinConfluenceForAutoTrade}");
                            }
                        }
                        
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
            
            prevRR_IsUp = RR_IsUp;
            prevDT_IsUp = DT_IsUp;
            prevVY_IsUp = VY_IsUp;
            prevET_IsUp = ET_IsUp;
            prevSW_IsUp = SW_IsUp;
            prevT3P_IsUp = T3P_IsUp;
            prevAAA_IsUp = AAA_IsUp;
            prevSB_IsUp = SB_IsUp;
            prevAIQ1_IsUp = AIQ1_IsUp;
            isFirstBar = false;
        }
    }
}
