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
    public class ActiveNikiTrader : Strategy
    {
        // ninZa indicator references (for VPS with licensed indicators)
        private object rubyRiver, vidyaPro, easyTrend, dragonTrend, solarWave, ninZaT3Pro;
        private FieldInfo rrIsUptrend, vyIsUptrend, etIsUptrend, dtPrevSignal, swIsUptrend, swCountWave, t3pIsUptrend;
        
        // Native AIQ_1 indicator reference (from AIQ folder)
        private object nativeAiq1;
        private FieldInfo nativeAiq1TrendState;  // Int32: 1=up, -1=down
        private bool useNativeAiq1;
        
        // Chart-attached equivalent indicator references
        private object chartAiq1Equivalent, chartRubyRiverEquiv, chartDragonTrendEquiv;
        private object chartVidyaProEquiv, chartEasyTrendEquiv, chartSolarWaveEquiv, chartT3ProEquiv;
        private PropertyInfo aiq1IsUptrend;
        private PropertyInfo rrEquivIsUptrend, dtEquivPrevSignal, vyEquivIsUptrend, etEquivIsUptrend;
        private PropertyInfo swEquivIsUptrend, swEquivCountWave, t3pEquivIsUptrend;
        private bool useChartAiq1, useChartRR, useChartDT, useChartVY, useChartET, useChartSW, useChartT3P;
        
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
        private CheckBox chkRubyRiver, chkDragonTrend, chkSolarWave, chkVIDYA, chkEasyTrend, chkT3Pro;
        private TextBlock lblRubyRiver, lblDragonTrend, lblSolarWave, lblVIDYA, lblEasyTrend, lblT3Pro;
        private TextBlock lblAIQ1Status, lblWindowStatus;
        private TextBlock lblTradeStatus, lblSessionStats, lblTriggerMode, lblLastSignal, lblSubtitle;
        private Border signalBorder;
        
        // Session tracking
        private int signalCount;
        private string lastSignalText = "";
        private string logFilePath;
        private StreamWriter logWriter;
        private string chartSessionId;
        
        // Daily P&L tracking
        private double dailyPnL = 0;
        private DateTime lastTradeDate = DateTime.MinValue;
        private bool dailyLossLimitHit = false;
        private int dailyTradeCount = 0;
        
        // Dynamic exit tracking
        private bool dynamicExitActive = false;
        private double entryPrice = 0;
        private double trailStopPrice = 0;
        private ATR atrIndicator;
        
        #region Parameters
        [NinjaScriptProperty]
        [Range(2, 6)]
        [Display(Name="Min Confluence Required", Description="Minimum indicators agreeing (2-6)", Order=1, GroupName="1. Signal Filters")]
        public int MinConfluenceRequired { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 3)]
        [Display(Name="Max Bars After Yellow Square", Description="Bars after AIQ1 flip to confirm with RubyRiver (0-3)", Order=2, GroupName="1. Signal Filters")]
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
        [Range(2, 6)]
        [Display(Name="Min Confluence For Auto Trade", Description="Higher confluence required to actually place orders (2-6)", Order=8, GroupName="1. Signal Filters")]
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
        [Range(0, 10)]
        [Display(Name="Stop Loss Buffer Ticks", Description="Extra ticks added to stop loss to reduce slippage (0=disabled)", Order=4, GroupName="11. Risk Management")]
        public int StopLossBufferTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Enable Dynamic Exit", Description="Let profits run when trend continues beyond TP", Order=1, GroupName="12. Dynamic Exit")]
        public bool EnableDynamicExit { get; set; }
        
        [NinjaScriptProperty]
        [Range(2, 6)]
        [Display(Name="Min Confluence To Stay", Description="Minimum confluence to keep position open past TP (2-6)", Order=2, GroupName="12. Dynamic Exit")]
        public int MinConfluenceToStay { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.25, 5.0)]
        [Display(Name="Trail Stop ATR Multiplier", Description="Trail stop distance in ATR multiples once past TP", Order=3, GroupName="12. Dynamic Exit")]
        public double TrailStopATRMultiplier { get; set; }
        
        [NinjaScriptProperty]
        [Range(10, 1000)]
        [Display(Name="Max Profit USD", Description="Maximum profit to ride before forcing exit", Order=4, GroupName="12. Dynamic Exit")]
        public double MaxProfitUSD { get; set; }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "ActiveNikiTrader";
                Description = "AIQ_1 trigger + RubyRiver confirmation with 6-indicator confluence filter (LONG + SHORT)";
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
                MinConfluenceRequired = 4;
                MaxBarsAfterYellowSquare = 3;
                MinSolarWaveCount = 1;
                StopLossUSD = 100;
                TakeProfitUSD = 60;
                CooldownBars = 10;
                EnableAutoTrading = false;
                MinConfluenceForAutoTrade = 5;
                
                // Trading hours filter - Optimized based on backtest analysis
                // Best performance: 09:00-10:59 (48% WR in first hour, 35% in second)
                // Exclude: 11:00-15:59 (0% WR in 11:00-11:59, 0% in 14:00-15:59)
                UseTradingHoursFilter = true;
                Session1StartHour = 7;
                Session1StartMinute = 0;
                Session1EndHour = 8;
                Session1EndMinute = 29;
                Session2StartHour = 9;
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
                
                // Indicator selection
                UseRubyRiver = true;
                UseDragonTrend = true;
                UseSolarWave = true;
                UseVIDYAPro = true;
                UseEasyTrend = true;
                UseT3Pro = true;
                
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
                ResetDailyPnLAtSessionStart = true;
                StopLossBufferTicks = 2;  // Add 2 ticks buffer to reduce slippage
                
                // Dynamic Exit - let profits run when trend continues
                EnableDynamicExit = true;
                MinConfluenceToStay = 4;  // Same as entry requirement
                TrailStopATRMultiplier = 1.5;
                MaxProfitUSD = 500;  // Force exit at $500 profit
            }
            else if (State == State.DataLoaded)
            {
                chartSessionId = DateTime.Now.ToString("HHmmss") + "_" + new Random().Next(1000, 9999);
                InitializeLogFile();
                
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
                
                LogAlways($"ActiveNikiTrader | Signal≥{MinConfluenceRequired} Trade≥{MinConfluenceForAutoTrade} | CD={CooldownBars} | SL=${StopLossUSD} TP=${TakeProfitUSD} | AutoTrade={EnableAutoTrading}");
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
            }
        }
        
        private void LoadNinZaIndicators()
        {
            if (ChartControl?.Indicators == null) 
            { 
                useHostedT3Pro = useHostedVIDYAPro = useHostedEasyTrend = true;
                useHostedRubyRiver = useHostedDragonTrend = useHostedSolarWave = true;
                useNativeAiq1 = useChartAiq1 = useChartRR = useChartDT = useChartVY = useChartET = useChartSW = useChartT3P = false;
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
                    
                    // Native AIQ_1 indicator (from AIQ folder) - uses trendState Int32 field
                    case "AIQ_1":
                        nativeAiq1 = ind;
                        // trendState: 1 = uptrend, -1 = downtrend
                        nativeAiq1TrendState = t.GetField("trendState", flagsPrivate);
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
                }
            }
            
            // Determine source priority for AIQ_1: native > chart-attached equivalent > hosted equivalent
            useNativeAiq1 = nativeAiq1 != null && nativeAiq1TrendState != null;
            useChartAiq1 = !useNativeAiq1 && chartAiq1Equivalent != null && aiq1IsUptrend != null;
            
            // Determine source priority for other indicators: ninZa > chart-attached equivalent > hosted equivalent
            useChartRR = chartRubyRiverEquiv != null && rrEquivIsUptrend != null;
            useChartDT = chartDragonTrendEquiv != null && dtEquivPrevSignal != null;
            useChartVY = chartVidyaProEquiv != null && vyEquivIsUptrend != null;
            useChartET = chartEasyTrendEquiv != null && etEquivIsUptrend != null;
            useChartSW = chartSolarWaveEquiv != null && swEquivIsUptrend != null;
            useChartT3P = chartT3ProEquiv != null && t3pEquivIsUptrend != null;
            
            // Use hosted only if neither ninZa nor chart-attached found
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
            LogAlways($"  ninZaRubyRiver:   {(rubyRiver != null ? "FOUND" : "not found")}");
            LogAlways($"  ninZaDragonTrend: {(dragonTrend != null ? "FOUND" : "not found")}");
            LogAlways($"  ninZaVIDYAPro:    {(vidyaPro != null ? "FOUND" : "not found")}");
            LogAlways($"  ninZaEasyTrend:   {(easyTrend != null ? "FOUND" : "not found")}");
            LogAlways($"  ninZaSolarWave:   {(solarWave != null ? "FOUND" : "not found")}");
            LogAlways($"  ninZaT3Pro:       {(ninZaT3Pro != null ? "FOUND" : "not found")}");
            LogAlways($"  AIQ_1 (native):   {(nativeAiq1 != null ? "FOUND" : "not found")}");
            
            LogAlways($"--- Indicator Sources (Priority: ninZa/native > chart > hosted) ---");
            LogAlways($"  RubyRiver:   {(rubyRiver != null ? "ninZa" : (useChartRR ? "CHART" : "hosted"))}");
            LogAlways($"  DragonTrend: {(dragonTrend != null ? "ninZa" : (useChartDT ? "CHART" : "hosted"))}");
            LogAlways($"  VIDYAPro:    {(vidyaPro != null ? "ninZa" : (useChartVY ? "CHART" : "hosted"))}");
            LogAlways($"  EasyTrend:   {(easyTrend != null ? "ninZa" : (useChartET ? "CHART" : "hosted"))}");
            LogAlways($"  SolarWave:   {(solarWave != null ? "ninZa" : (useChartSW ? "CHART" : "hosted"))}");
            LogAlways($"  T3Pro:       {(ninZaT3Pro != null ? "ninZa" : (useChartT3P ? "CHART" : "hosted"))}");
            LogAlways($"  AIQ_1:       {(useNativeAiq1 ? "NATIVE" : (useChartAiq1 ? "CHART" : "hosted"))}");
            
            if (ChartControl?.Indicators != null)
            {
                LogAlways($"--- All Chart Indicators ({ChartControl.Indicators.Count}) ---");
                foreach (var ind in ChartControl.Indicators)
                    LogAlways($"  - {ind.GetType().Name}");
            }
            LogAlways($"--------------------------------");
        }
        
        private void LogAlways(string msg)
        {
            Print(msg);
            if (logWriter != null)
                try { logWriter.WriteLine($"{DateTime.Now:HH:mm:ss} | {msg}"); } catch { }
        }
        
        // Helper methods for reflection-based indicator reading
        private bool GetBool(object o, FieldInfo f) { try { return o != null && f != null && (bool)f.GetValue(o); } catch { return false; } }
        private double GetDbl(object o, FieldInfo f) { try { return o != null && f != null ? (double)f.GetValue(o) : 0; } catch { return 0; } }
        private int GetInt(object o, FieldInfo f) { try { return o != null && f != null ? (int)f.GetValue(o) : 0; } catch { return 0; } }
        
        // Indicator value accessors - Priority: ninZa > chart-attached > hosted
        [Browsable(false)] public bool RR_IsUp => rubyRiver != null ? GetBool(rubyRiver, rrIsUptrend) : (useChartRR ? GetChartBool(chartRubyRiverEquiv, rrEquivIsUptrend) : (rubyRiverEquivalent?.IsUptrend ?? false));
        [Browsable(false)] public bool VY_IsUp => vidyaPro != null ? GetBool(vidyaPro, vyIsUptrend) : (useChartVY ? GetChartBool(chartVidyaProEquiv, vyEquivIsUptrend) : (vidyaProEquivalent?.IsUptrend ?? false));
        [Browsable(false)] public bool ET_IsUp => easyTrend != null ? GetBool(easyTrend, etIsUptrend) : (useChartET ? GetChartBool(chartEasyTrendEquiv, etEquivIsUptrend) : (easyTrendEquivalent?.IsUptrend ?? false));
        [Browsable(false)] public double DT_Signal => dragonTrend != null ? GetDbl(dragonTrend, dtPrevSignal) : (useChartDT ? GetChartDbl(chartDragonTrendEquiv, dtEquivPrevSignal) : (dragonTrendEquivalent?.PrevSignal ?? 0));
        [Browsable(false)] public bool DT_IsUp => DT_Signal > 0;
        [Browsable(false)] public bool DT_IsDown => DT_Signal < 0;
        [Browsable(false)] public bool SW_IsUp => solarWave != null ? GetBool(solarWave, swIsUptrend) : (useChartSW ? GetChartBool(chartSolarWaveEquiv, swEquivIsUptrend) : (solarWaveEquivalent?.IsUptrend ?? false));
        [Browsable(false)] public int SW_Count => solarWave != null ? GetInt(solarWave, swCountWave) : (useChartSW ? GetChartInt(chartSolarWaveEquiv, swEquivCountWave) : (solarWaveEquivalent?.CountWave ?? 0));
        [Browsable(false)] public bool T3P_IsUp => ninZaT3Pro != null ? GetBool(ninZaT3Pro, t3pIsUptrend) : (useChartT3P ? GetChartBool(chartT3ProEquiv, t3pEquivIsUptrend) : (t3ProEquivalent?.IsUptrend ?? false));
        
        // AIQ_1 trigger indicator - Priority: native > chart-attached equivalent > hosted equivalent
        // Native AIQ_1 uses trendState: 1 = uptrend, -1 = downtrend
        [Browsable(false)] public bool AIQ1_IsUp 
        {
            get 
            {
                if (useNativeAiq1)
                {
                    // trendState is Int32: 1 = up, -1 = down
                    return GetInt(nativeAiq1, nativeAiq1TrendState) > 0;
                }
                if (useChartAiq1)
                    return GetChartBool(chartAiq1Equivalent, aiq1IsUptrend);
                return aiq1Equivalent?.IsUptrend ?? false;
            }
        }
        
        // Helper methods for chart-attached indicator reading (via PropertyInfo)
        private bool GetChartBool(object o, PropertyInfo p) { try { return o != null && p != null && (bool)p.GetValue(o); } catch { return false; } }
        private double GetChartDbl(object o, PropertyInfo p) { try { return o != null && p != null ? (double)p.GetValue(o) : 0; } catch { return 0; } }
        private int GetChartInt(object o, PropertyInfo p) { try { return o != null && p != null ? (int)p.GetValue(o) : 0; } catch { return 0; } }
        
        private int GetEnabledCount() => (UseRubyRiver?1:0)+(UseDragonTrend?1:0)+(UseSolarWave?1:0)+(UseVIDYAPro?1:0)+(UseEasyTrend?1:0)+(UseT3Pro?1:0);
        
        private (int bull, int bear, int total) GetConfluence()
        {
            int bull = 0, bear = 0, total = 0;
            if (UseRubyRiver) { total++; if (RR_IsUp) bull++; else bear++; }
            if (UseDragonTrend) { total++; if (DT_IsUp) bull++; else if (DT_IsDown) bear++; }
            if (UseSolarWave) { total++; if (SW_IsUp && SW_Count >= MinSolarWaveCount) bull++; else if (!SW_IsUp && SW_Count <= -MinSolarWaveCount) bear++; }
            if (UseVIDYAPro) { total++; if (VY_IsUp) bull++; else bear++; }
            if (UseEasyTrend) { total++; if (ET_IsUp) bull++; else bear++; }
            if (UseT3Pro) { total++; if (T3P_IsUp) bull++; else bear++; }
            return (bull, bear, total);
        }
        
        private bool IsTradingHoursAllowed(DateTime barTime)
        {
            if (!UseTradingHoursFilter) return true;
            
            int currentMinutes = barTime.Hour * 60 + barTime.Minute;
            int session1Start = Session1StartHour * 60 + Session1StartMinute;
            int session1End = Session1EndHour * 60 + Session1EndMinute;
            int session2Start = Session2StartHour * 60 + Session2StartMinute;
            int session2End = Session2EndHour * 60 + Session2EndMinute;
            
            bool inSession1 = currentMinutes >= session1Start && currentMinutes <= session1End;
            bool inSession2 = currentMinutes >= session2Start && currentMinutes <= session2End;
            
            return inSession1 || inSession2;
        }
        
        private string GetTradingHoursString()
        {
            return $"{Session1StartHour:D2}:{Session1StartMinute:D2}-{Session1EndHour:D2}:{Session1EndMinute:D2}, {Session2StartHour:D2}:{Session2StartMinute:D2}-{Session2EndHour:D2}:{Session2EndMinute:D2}";
        }

        #region Chart Panel
        private void CreateControlPanel()
        {
            try
            {
                string settingsDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8", "settings");
                panelSettingsFile = System.IO.Path.Combine(settingsDir, "ActiveNikiTrader_PanelSettings.txt");
                panelTransform = new TranslateTransform(0, 0);
                panelScale = new ScaleTransform(1, 1);
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(panelScale);
                transformGroup.Children.Add(panelTransform);
                LoadPanelSettings();

                controlPanel = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(10, 0, 0, 30),
                    Background = new SolidColorBrush(Color.FromArgb(115, 30, 30, 40)),
                    MinWidth = 200,
                    RenderTransform = transformGroup,
                    RenderTransformOrigin = new Point(0, 1),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                controlPanel.MouseLeftButtonDown += Panel_MouseLeftButtonDown;
                controlPanel.MouseLeftButtonUp += Panel_MouseLeftButtonUp;
                controlPanel.MouseMove += Panel_MouseMove;

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 100)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8)
                };
                
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = "ActiveNiki Trader", FontWeight = FontWeights.Bold, Foreground = Brushes.Cyan, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) });
                lblSubtitle = new TextBlock { Foreground = Brushes.LightGray, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,6) };
                stack.Children.Add(lblSubtitle);
                
                stack.Children.Add(new TextBlock { Text = "─ Confluence ─", Foreground = Brushes.Gray, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,2,0,2) });
                stack.Children.Add(CreateRow("Ruby River", ref chkRubyRiver, ref lblRubyRiver, UseRubyRiver));
                stack.Children.Add(CreateRow("Dragon Trend", ref chkDragonTrend, ref lblDragonTrend, UseDragonTrend));
                stack.Children.Add(CreateRow("VIDYA Pro", ref chkVIDYA, ref lblVIDYA, UseVIDYAPro));
                stack.Children.Add(CreateRow("Easy Trend", ref chkEasyTrend, ref lblEasyTrend, UseEasyTrend));
                stack.Children.Add(CreateRow("Solar Wave", ref chkSolarWave, ref lblSolarWave, UseSolarWave));
                stack.Children.Add(CreateRow("T3 Pro", ref chkT3Pro, ref lblT3Pro, UseT3Pro));
                
                stack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(0,6,0,6) });
                stack.Children.Add(new TextBlock { Text = "─ Trigger ─", Foreground = Brushes.Orange, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,2,0,2) });
                
                var aiqRow = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                aiqRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                aiqRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                var lblAIQ1Name = new TextBlock { Text = "AIQ_1 (Yellow □)", Foreground = Brushes.Orange, FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(lblAIQ1Name, 0); aiqRow.Children.Add(lblAIQ1Name);
                lblAIQ1Status = new TextBlock { Text = "---", Foreground = Brushes.Gray, FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(lblAIQ1Status, 1); aiqRow.Children.Add(lblAIQ1Status);
                stack.Children.Add(aiqRow);
                
                lblWindowStatus = new TextBlock { Text = "Window: CLOSED", Foreground = Brushes.Gray, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,2,0,2) };
                stack.Children.Add(lblWindowStatus);
                
                stack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(0,6,0,6) });

                lblTriggerMode = new TextBlock { Text = $"Signal≥{MinConfluenceRequired} Trade≥{MinConfluenceForAutoTrade} CD={CooldownBars}", Foreground = Brushes.LightGray, FontSize = 9 };
                lblTradeStatus = new TextBlock { Text = EnableAutoTrading ? "⚡ AUTO TRADING ON" : "Mode: Signal Only", Foreground = EnableAutoTrading ? Brushes.Lime : Brushes.Cyan, FontWeight = FontWeights.Bold, FontSize = 10, Margin = new Thickness(0,2,0,2) };
                lblSessionStats = new TextBlock { Text = "Signals: 0", Foreground = Brushes.LightGray, FontSize = 9 };

                stack.Children.Add(lblTriggerMode);
                stack.Children.Add(lblTradeStatus);
                stack.Children.Add(lblSessionStats);
                
                stack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(0,6,0,6) });
                
                signalBorder = new Border { BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(3), Padding = new Thickness(4) };
                lblLastSignal = new TextBlock { Text = "Waiting for Yellow □...", Foreground = Brushes.Gray, FontSize = 9, TextWrapping = TextWrapping.Wrap };
                signalBorder.Child = lblLastSignal;
                stack.Children.Add(signalBorder);
                
                border.Child = stack;
                controlPanel.Children.Add(border);

                resizeGrip = new Border { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.SizeNWSE, Margin = new Thickness(0, 0, 2, 2) };
                var gripCanvas = new Canvas { Width = 12, Height = 12 };
                for (int i = 0; i < 3; i++)
                {
                    var line = new Line { X1 = 10 - i * 4, Y1 = 10, X2 = 10, Y2 = 10 - i * 4, Stroke = new SolidColorBrush(Color.FromRgb(120, 120, 140)), StrokeThickness = 1 };
                    gripCanvas.Children.Add(line);
                }
                resizeGrip.Child = gripCanvas;
                resizeGrip.MouseLeftButtonDown += ResizeGrip_MouseLeftButtonDown;
                resizeGrip.MouseLeftButtonUp += ResizeGrip_MouseLeftButtonUp;
                resizeGrip.MouseMove += ResizeGrip_MouseMove;
                controlPanel.Children.Add(resizeGrip);

                UIElementCollection panelHolder = (ChartControl.Parent as Grid)?.Children;
                if (panelHolder != null) panelHolder.Add(controlPanel);
                panelActive = true;
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
                    if (resizeGrip != null)
                    {
                        resizeGrip.MouseLeftButtonDown -= ResizeGrip_MouseLeftButtonDown;
                        resizeGrip.MouseLeftButtonUp -= ResizeGrip_MouseLeftButtonUp;
                        resizeGrip.MouseMove -= ResizeGrip_MouseMove;
                    }
                    UIElementCollection panelHolder = (ChartControl?.Parent as Grid)?.Children;
                    if (panelHolder != null && panelHolder.Contains(controlPanel))
                        panelHolder.Remove(controlPanel);
                    panelActive = false;
                }
            }
            catch { }
        }

        private void Panel_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            isDragging = true;
            dragStartPoint = e.GetPosition(ChartControl?.Parent as UIElement);
            dragStartPoint.X -= panelTransform.X;
            dragStartPoint.Y -= panelTransform.Y;
            controlPanel.CaptureMouse();
            e.Handled = true;
        }

        private void Panel_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (isDragging) { isDragging = false; controlPanel.ReleaseMouseCapture(); SavePanelSettings(); e.Handled = true; }
        }

        private void Panel_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (isDragging)
            {
                Point currentPoint = e.GetPosition(ChartControl?.Parent as UIElement);
                double newX = currentPoint.X - dragStartPoint.X;
                double newY = currentPoint.Y - dragStartPoint.Y;
                var parent = ChartControl?.Parent as FrameworkElement;
                if (parent != null && controlPanel != null)
                {
                    double panelWidth = controlPanel.ActualWidth > 0 ? controlPanel.ActualWidth : 200;
                    double panelHeight = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : 300;
                    double minX = -10, maxX = parent.ActualWidth - panelWidth - 10;
                    double minY = -(parent.ActualHeight - panelHeight - 30), maxY = 0;
                    newX = Math.Max(minX, Math.Min(maxX, newX));
                    newY = Math.Max(minY, Math.Min(maxY, newY));
                }
                panelTransform.X = newX;
                panelTransform.Y = newY;
                e.Handled = true;
            }
        }

        private void ResizeGrip_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            isResizing = true;
            resizeStartPoint = e.GetPosition(ChartControl?.Parent as UIElement);
            resizeStartWidth = panelScale.ScaleX;
            resizeStartHeight = panelScale.ScaleY;
            resizeGrip.CaptureMouse();
            e.Handled = true;
        }

        private void ResizeGrip_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (isResizing) { isResizing = false; resizeGrip.ReleaseMouseCapture(); SavePanelSettings(); e.Handled = true; }
        }

        private void ResizeGrip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (isResizing)
            {
                Point currentPoint = e.GetPosition(ChartControl?.Parent as UIElement);
                double deltaX = currentPoint.X - resizeStartPoint.X;
                double deltaY = currentPoint.Y - resizeStartPoint.Y;
                double baseWidth = controlPanel.ActualWidth > 0 ? controlPanel.ActualWidth / panelScale.ScaleX : 200;
                double avgDelta = (deltaX - deltaY) / 2;
                double newScale = resizeStartWidth + avgDelta / baseWidth;
                newScale = Math.Max(0.5, Math.Min(2.0, newScale));
                panelScale.ScaleX = newScale;
                panelScale.ScaleY = newScale;
                e.Handled = true;
            }
        }

        private void SavePanelSettings()
        {
            try
            {
                if (string.IsNullOrEmpty(panelSettingsFile)) return;
                string dir = System.IO.Path.GetDirectoryName(panelSettingsFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(panelSettingsFile, $"{panelTransform.X},{panelTransform.Y},{panelScale.ScaleX},{panelScale.ScaleY}");
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
                { panelTransform.X = x; panelTransform.Y = y; }
                if (parts.Length >= 4 && double.TryParse(parts[2], out double scaleX) && double.TryParse(parts[3], out double scaleY))
                { panelScale.ScaleX = Math.Max(0.5, Math.Min(2.0, scaleX)); panelScale.ScaleY = Math.Max(0.5, Math.Min(2.0, scaleY)); }
            }
            catch { }
        }
        
        private void UpdatePanel()
        {
            if (!panelActive || ChartControl == null) return;
            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                int enabled = GetEnabledCount();
                if (lblSubtitle != null)
                    lblSubtitle.Text = enabled == 0 ? "No indicators" : $"Min {MinConfluenceRequired}/{enabled} for signal";

                UpdLbl(lblRubyRiver, RR_IsUp, UseRubyRiver);
                UpdLbl(lblDragonTrend, DT_IsUp, UseDragonTrend);
                UpdLbl(lblSolarWave, SW_IsUp, UseSolarWave);
                UpdLbl(lblVIDYA, VY_IsUp, UseVIDYAPro);
                UpdLbl(lblEasyTrend, ET_IsUp, UseEasyTrend);
                UpdLbl(lblT3Pro, T3P_IsUp, UseT3Pro);
                
                if (lblAIQ1Status != null)
                {
                    lblAIQ1Status.Text = AIQ1_IsUp ? "UP" : "DN";
                    lblAIQ1Status.Foreground = AIQ1_IsUp ? Brushes.Lime : Brushes.Red;
                }
                
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
                string dailyPnLText = EnableDailyLossLimit ? $" | Day: ${dailyPnL:F0}" : "";
                string limitHitText = dailyLossLimitHit ? " 🛑STOPPED" : "";
                if (lblSessionStats != null) lblSessionStats.Text = $"Signals: {signalCount} | Bull:{bull} Bear:{bear}/{total}{dailyPnLText}{limitHitText}";

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
                        lblLastSignal.Text = $"Bull OK ({bull}/{total})\nWaiting for Yellow □...";
                        lblLastSignal.FontWeight = FontWeights.Normal;
                        lblLastSignal.Foreground = Brushes.Lime;
                        signalBorder.BorderBrush = Brushes.Lime;
                        signalBorder.Background = Brushes.Transparent;
                    }
                    else if (bear >= MinConfluenceRequired)
                    {
                        lblLastSignal.Text = $"Bear OK ({bear}/{total})\nWaiting for Orange □...";
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
        
        private void UpdateSignalDisplay(string trigger, int confluenceCount, int total, DateTime t, bool isLong)
        {
            signalCount++;
            string dir = isLong ? "LONG" : "SHORT";
            lastSignalText = $"{dir} @ {confluenceCount}/{total} [{trigger}] {t:HH:mm:ss}";
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

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade || !indicatorsReady) return;
            
            DateTime barTime = Time[0];
            
            // Daily P&L reset check - reset at start of new trading day
            if (ResetDailyPnLAtSessionStart && barTime.Date != lastTradeDate.Date)
            {
                if (dailyPnL != 0 || dailyTradeCount > 0)
                    PrintAndLog($"📊 NEW DAY: Resetting Daily P&L (was ${dailyPnL:F2}, {dailyTradeCount} trades)");
                dailyPnL = 0;
                dailyTradeCount = 0;
                dailyLossLimitHit = false;
                lastTradeDate = barTime.Date;
            }
            
            // Daily loss limit check
            if (EnableDailyLossLimit && dailyLossLimitHit)
            {
                // Already hit limit - no more trading today
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
            
            // ═══════════════════════════════════════════════════════════════════
            // DYNAMIC EXIT MANAGEMENT - Let profits run when trend continues
            // ═══════════════════════════════════════════════════════════════════
            if (EnableDynamicExit && Position.MarketPosition != MarketPosition.Flat && entryPrice > 0)
            {
                double currentPrice = Close[0];
                double pointValue = Instrument.MasterInstrument.PointValue;
                double tpPoints = pointValue > 0 ? TakeProfitUSD / pointValue : 10;
                double maxProfitPoints = pointValue > 0 ? MaxProfitUSD / pointValue : 25;
                double atrValue = atrIndicator[0];
                
                var (bull, bear, total) = GetConfluence();
                bool isLong = Position.MarketPosition == MarketPosition.Long;
                
                // Calculate unrealized P&L
                double unrealizedPoints = isLong ? (currentPrice - entryPrice) : (entryPrice - currentPrice);
                double unrealizedPnL = unrealizedPoints * pointValue;
                
                // Check if we've reached TP level
                bool pastTpLevel = unrealizedPoints >= tpPoints;
                
                // Check for max profit limit
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
                    // We're past TP level - check confluence to decide whether to stay
                    bool confluenceConfirmsTrend = isLong ? (bull >= MinConfluenceToStay) : (bear >= MinConfluenceToStay);
                    bool trendStillValid = isLong ? RR_IsUp : !RR_IsUp;
                    
                    if (!dynamicExitActive)
                    {
                        // First time past TP - decide whether to activate dynamic mode
                        if (confluenceConfirmsTrend && trendStillValid)
                        {
                            dynamicExitActive = true;
                            // Set initial trail stop at entry + some profit
                            double trailDistance = atrValue * TrailStopATRMultiplier;
                            trailStopPrice = isLong ? (currentPrice - trailDistance) : (currentPrice + trailDistance);
                            PrintAndLog($"🚀 DYNAMIC MODE ACTIVATED @ {barTime:HH:mm:ss} | P&L=${unrealizedPnL:F2} | Trail={trailStopPrice:F2} | Conf={bull}/{bear}");
                        }
                        else
                        {
                            // Confluence doesn't confirm - take profit now
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
                        // Already in dynamic mode - update trail stop and check exit conditions
                        double trailDistance = atrValue * TrailStopATRMultiplier;
                        
                        if (isLong)
                        {
                            double newTrailStop = currentPrice - trailDistance;
                            if (newTrailStop > trailStopPrice)
                            {
                                trailStopPrice = newTrailStop;
                                PrintAndLog($"📈 TRAIL STOP UPDATED @ {barTime:HH:mm:ss} | New Stop={trailStopPrice:F2} | P&L=${unrealizedPnL:F2}");
                            }
                            
                            // Check exit conditions: trail stop hit OR trend reversed
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
                                PrintAndLog($"📉 TRAIL STOP UPDATED @ {barTime:HH:mm:ss} | New Stop={trailStopPrice:F2} | P&L=${unrealizedPnL:F2}");
                            }
                            
                            // Check exit conditions: trail stop hit OR trend reversed
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
            
            // Reset dynamic exit tracking when flat
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                entryPrice = 0;
                dynamicExitActive = false;
                trailStopPrice = 0;
            }
            
            // Increment bar-based cooldown counter
            if (barsSinceLastSignal >= 0)
                barsSinceLastSignal++;
            
            // Check cooldown - support both bar-based and time-based
            bool inCooldown = false;
            string cooldownStatus = "";
            
            if (UseTimeBasedCooldown)
            {
                // Time-based cooldown (better for UniRenko)
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
                // Bar-based cooldown (original behavior)
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
                    PrintAndLog($"LONG window expired @ {barTime:HH:mm:ss} | No RR UP within {MaxBarsAfterYellowSquare} bars");
                    barsSinceYellowSquare = -1;
                }
            }
            else if (barsSinceOrangeSquare >= 0)
            {
                barsSinceOrangeSquare++;
                if (barsSinceOrangeSquare > MaxBarsAfterYellowSquare)
                {
                    PrintAndLog($"SHORT window expired @ {barTime:HH:mm:ss} | No RR DN within {MaxBarsAfterYellowSquare} bars");
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
                if (RR_IsUp)
                {
                    var (bull, bear, total) = GetConfluence();
                    
                    if (bull >= MinConfluenceRequired)
                    {
                        LogSignal("LONG", "YellowSquare+RR", barTime, bull, total);
                        UpdateSignalDisplay("YellowSquare+RR", bull, total, barTime, true);
                        
                        if (EnableAutoTrading && Position.MarketPosition == MarketPosition.Flat)
                        {
                            if (bull >= MinConfluenceForAutoTrade)
                            {
                                if (IsTradingHoursAllowed(barTime))
                                {
                                    double stopPoints = Instrument.MasterInstrument.PointValue > 0 ? StopLossUSD / Instrument.MasterInstrument.PointValue : 5;
                                    double tpPoints = Instrument.MasterInstrument.PointValue > 0 ? TakeProfitUSD / Instrument.MasterInstrument.PointValue : 3;
                                    
                                    // Add buffer to stop loss to reduce slippage
                                    double slTicks = (stopPoints / TickSize) + StopLossBufferTicks;
                                    
                                    if (EnableDynamicExit)
                                    {
                                        // Dynamic exit: only set stop loss, manage TP manually
                                        SetStopLoss("Long", CalculationMode.Ticks, slTicks, true);
                                        entryPrice = GetCurrentAsk();
                                        dynamicExitActive = false;
                                        trailStopPrice = 0;
                                    }
                                    else
                                    {
                                        // Fixed TP mode
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
                        lastSignalTime = barTime;  // Track time for time-based cooldown
                    }
                    else
                    {
                        PrintAndLog($"RR is UP but confluence {bull}/{total} < {MinConfluenceRequired} @ {barTime:HH:mm:ss}");
                    }
                }
            }
            
            if (barsSinceOrangeSquare >= 0 && barsSinceOrangeSquare <= MaxBarsAfterYellowSquare)
            {
                if (!RR_IsUp)
                {
                    var (bull, bear, total) = GetConfluence();
                    
                    if (bear >= MinConfluenceRequired)
                    {
                        LogSignal("SHORT", "OrangeSquare+RR", barTime, bear, total);
                        UpdateSignalDisplay("OrangeSquare+RR", bear, total, barTime, false);
                        
                        if (EnableAutoTrading && Position.MarketPosition == MarketPosition.Flat)
                        {
                            if (bear >= MinConfluenceForAutoTrade)
                            {
                                if (IsTradingHoursAllowed(barTime))
                                {
                                    double stopPoints = Instrument.MasterInstrument.PointValue > 0 ? StopLossUSD / Instrument.MasterInstrument.PointValue : 5;
                                    double tpPoints = Instrument.MasterInstrument.PointValue > 0 ? TakeProfitUSD / Instrument.MasterInstrument.PointValue : 3;
                                    
                                    // Add buffer to stop loss to reduce slippage
                                    double slTicks = (stopPoints / TickSize) + StopLossBufferTicks;
                                    
                                    if (EnableDynamicExit)
                                    {
                                        // Dynamic exit: only set stop loss, manage TP manually
                                        SetStopLoss("Short", CalculationMode.Ticks, slTicks, true);
                                        entryPrice = GetCurrentBid();
                                        dynamicExitActive = false;
                                        trailStopPrice = 0;
                                    }
                                    else
                                    {
                                        // Fixed TP mode
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
                        lastSignalTime = barTime;  // Track time for time-based cooldown
                    }
                    else
                    {
                        PrintAndLog($"RR is DN but bear confluence {bear}/{total} < {MinConfluenceRequired} @ {barTime:HH:mm:ss}");
                    }
                }
            }
            
            prevAIQ1_IsUp = AIQ1_IsUp;
            isFirstBar = false;
        }
        
        private void LogSignal(string dir, string trigger, DateTime t, int confluenceCount, int total)
        {
            double askPrice = GetCurrentAsk();
            double bidPrice = GetCurrentBid();
            double pointValue = Instrument.MasterInstrument.PointValue;
            double stopPoints = pointValue > 0 ? StopLossUSD / pointValue : 0;
            double tpPoints = pointValue > 0 ? TakeProfitUSD / pointValue : 0;
            
            double entryPrice, stopPrice, tpPrice;
            int barsAfterSquare;
            
            if (dir == "LONG")
            {
                entryPrice = askPrice;
                stopPrice = askPrice - stopPoints;
                tpPrice = askPrice + tpPoints;
                barsAfterSquare = barsSinceYellowSquare;
            }
            else
            {
                entryPrice = bidPrice;
                stopPrice = bidPrice + stopPoints;
                tpPrice = bidPrice - tpPoints;
                barsAfterSquare = barsSinceOrangeSquare;
            }
            
            string instrumentName = Instrument.FullName;
            string squareType = dir == "LONG" ? "Yellow□" : "Orange□";
            
            PrintAndLog($"");
            PrintAndLog($"╔════════════════════════════════════════════════════════╗");
            PrintAndLog($"║  *** {dir} SIGNAL @ {t:HH:mm:ss} ***");
            PrintAndLog($"╠════════════════════════════════════════════════════════╣");
            PrintAndLog($"║  Instrument: {instrumentName}");
            PrintAndLog($"║  Ask: {askPrice:F2}    Bid: {bidPrice:F2}");
            PrintAndLog($"║  STOP: {stopPrice:F2}  (${StopLossUSD:F0} = {stopPoints:F2} pts)");
            PrintAndLog($"║  TP:   {tpPrice:F2}  (${TakeProfitUSD:F0} = {tpPoints:F2} pts)");
            PrintAndLog($"╠════════════════════════════════════════════════════════╣");
            PrintAndLog($"║  Trigger: {trigger}");
            PrintAndLog($"║  Confluence: {confluenceCount}/{total}");
            PrintAndLog($"║  RR={Ts(RR_IsUp)} DT={DT_Signal:F0} VY={Ts(VY_IsUp)} ET={Ts(ET_IsUp)} SW={SW_Count} T3P={Ts(T3P_IsUp)}");
            PrintAndLog($"║  AIQ1={Ts(AIQ1_IsUp)} | Bars after {squareType}: {barsAfterSquare}");
            PrintAndLog($"╚════════════════════════════════════════════════════════╝");
        }
        
        private double GetCurrentAsk()
        {
            if (BarsInProgress == 0 && GetCurrentAsk(0) > 0)
                return GetCurrentAsk(0);
            return Close[0];
        }
        
        private double GetCurrentBid()
        {
            if (BarsInProgress == 0 && GetCurrentBid(0) > 0)
                return GetCurrentBid(0);
            return Close[0];
        }
        
        private string Ts(bool up) => up ? "UP" : "DN";
        
        #region Logging
        private void InitializeLogFile()
        {
            try
            {
                string dir = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "log");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                logFilePath = System.IO.Path.Combine(dir, $"ActiveNikiTrader_{DateTime.Now:yyyy-MM-dd}_{chartSessionId}.txt");
                logWriter = new StreamWriter(logFilePath, true) { AutoFlush = true };
                logWriter.WriteLine($"\n=== ActiveNikiTrader Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                if (UniRenkoMode)
                {
                    logWriter.WriteLine($"    *** UNIRENKO MODE ***");
                    logWriter.WriteLine($"    Cooldown: {(UseTimeBasedCooldown ? $"{CooldownSeconds} seconds (time-based)" : $"{CooldownBars} bars")}");
                }
                logWriter.WriteLine($"    Signal Filter: MinConf={MinConfluenceRequired}/6, MaxBars={MaxBarsAfterYellowSquare}, Cooldown={CooldownBars}");
                logWriter.WriteLine($"    Auto Trade: {(EnableAutoTrading ? "ON" : "OFF")} | MinConf for Trade={MinConfluenceForAutoTrade}/6");
                logWriter.WriteLine($"    Risk: SL=${StopLossUSD:F0}, TP=${TakeProfitUSD:F0}");
                if (EnableDailyLossLimit)
                    logWriter.WriteLine($"    Daily Loss Limit: ${DailyLossLimitUSD:F0}");
                if (UseTradingHoursFilter)
                    logWriter.WriteLine($"    Trading Hours: {GetTradingHoursString()}");
                else
                    logWriter.WriteLine($"    Trading Hours: ALL (filter disabled)");
                if (CloseBeforeNews)
                    logWriter.WriteLine($"    Auto-Close Before News: {NewsCloseHour:D2}:{NewsCloseMinute:D2}");
                if (CloseAtEndOfDay)
                    logWriter.WriteLine($"    Auto-Close EOD: {EODCloseHour:D2}:{EODCloseMinute:D2}");
                logWriter.WriteLine($"    LONG:  Yellow□ (AIQ1 UP) → RR UP → Bull Confluence ≥ {MinConfluenceRequired}");
                logWriter.WriteLine($"    SHORT: Orange□ (AIQ1 DN) → RR DN → Bear Confluence ≥ {MinConfluenceRequired}\n");
            }
            catch { }
        }
        
        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            // Track P&L when a position is closed
            if (execution.Order != null && execution.Order.OrderState == OrderState.Filled)
            {
                // Check if this is an exit (position is now flat and order name suggests exit)
                string orderName = execution.Order.Name ?? "";
                bool isExit = Position.MarketPosition == MarketPosition.Flat && 
                    (orderName.Contains("Stop") || orderName.Contains("Profit") || orderName.Contains("Exit") || 
                     execution.Order.OrderAction == OrderAction.Sell || execution.Order.OrderAction == OrderAction.BuyToCover);
                
                if (isExit && SystemPerformance.AllTrades.Count > 0)
                {
                    // Get the most recent completed trade
                    var lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                    double tradePnL = lastTrade.ProfitCurrency;
                    
                    dailyPnL += tradePnL;
                    dailyTradeCount++;
                    
                    string pnlIcon = tradePnL >= 0 ? "✅" : "❌";
                    PrintAndLog($"{pnlIcon} TRADE CLOSED: P&L ${tradePnL:F2} | Daily P&L: ${dailyPnL:F2} ({dailyTradeCount} trades)");
                    
                    // Play sound on trade close
                    if (EnableSoundAlert)
                        try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                    
                    // Check if daily loss limit hit
                    if (EnableDailyLossLimit && dailyPnL <= -DailyLossLimitUSD)
                    {
                        dailyLossLimitHit = true;
                        PrintAndLog($"🛑 DAILY LOSS LIMIT HIT: ${dailyPnL:F2} exceeds -${DailyLossLimitUSD:F2} limit. Trading stopped for today.");
                        if (EnableSoundAlert)
                            try { System.Media.SystemSounds.Hand.Play(); } catch { }
                    }
                }
            }
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
        #endregion
    }
}
