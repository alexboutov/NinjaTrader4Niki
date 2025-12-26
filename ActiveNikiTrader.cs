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
        private object rubyRiver, vidyaPro, easyTrend, dragonTrend, solarWave, ninZaT3Pro;
        private T3ProEquivalent t3ProEquivalent;
        private VIDYAProEquivalent vidyaProEquivalent;
        private EasyTrendEquivalent easyTrendEquivalent;
        private RubyRiverEquivalent rubyRiverEquivalent;
        private DragonTrendEquivalent dragonTrendEquivalent;
        private SolarWaveEquivalent solarWaveEquivalent;
        private FieldInfo rrIsUptrend, vyIsUptrend, etIsUptrend, dtPrevSignal, swIsUptrend, swCountWave, t3pIsUptrend;
        private bool prevRR_IsUp, prevDT_IsUp, indicatorsReady, isFirstBar = true;
        private bool useHostedT3Pro, useHostedVIDYAPro, useHostedEasyTrend, useHostedRubyRiver, useHostedDragonTrend, useHostedSolarWave;
        private DateTime lastRR_FlipTime = DateTime.MinValue, lastDT_FlipTime = DateTime.MinValue;
        private Queue<DateTime> recentFlips = new Queue<DateTime>();
        
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
        private TextBlock lblTradeStatus, lblSessionStats, lblTriggerMode, lblLastSignal, lblSubtitle;
        private Border signalBorder;
        
        private int signalCount;
        private string lastSignalText = "";
        private string logFilePath;
        private StreamWriter logWriter;
        private string chartSessionId;
        private TimeSpan startTime = new TimeSpan(6, 50, 0), endTime = new TimeSpan(11, 59, 0);
        
        #region Parameters
        [NinjaScriptProperty][Range(1, 6)][Display(Name="Min Indicators Required", Order=1, GroupName="1. Signal Filters")]
        public int MinIndicatorsRequired { get; set; }
        [NinjaScriptProperty][Range(1, 20)][Display(Name="Min Solar Wave Count", Order=2, GroupName="1. Signal Filters")]
        public int MinSolarWaveCount { get; set; }
        [NinjaScriptProperty][Display(Name="Require Ruby River Trigger", Order=3, GroupName="1. Signal Filters")]
        public bool RequireRubyRiverTrigger { get; set; }
        [NinjaScriptProperty][Display(Name="Enable DT After RR Trigger", Order=4, GroupName="1. Signal Filters")]
        public bool EnableDTAfterRR { get; set; }
        [NinjaScriptProperty][Range(10, 300)][Display(Name="Min Seconds Since Flip", Order=5, GroupName="1. Signal Filters")]
        public int MinSecondsSinceFlip { get; set; }
        [NinjaScriptProperty][Range(1, 20)][Display(Name="Max Flips Per Minute", Order=6, GroupName="1. Signal Filters")]
        public int MaxFlipsPerMinute { get; set; }
        
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
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "ActiveNikiTrader";
                Description = "Monitors indicators and displays signals - Trader Strategy";
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
                
                MinIndicatorsRequired = 4;
                MinSolarWaveCount = 1;
                RequireRubyRiverTrigger = true;
                EnableDTAfterRR = true;
                MinSecondsSinceFlip = 30;
                MaxFlipsPerMinute = 6;
                
                UseRubyRiver = true;
                UseDragonTrend = true;
                UseSolarWave = true;
                UseVIDYAPro = true;
                UseEasyTrend = true;
                UseT3Pro = true;
                
                T3ProPeriod = 14;
                T3ProTCount = 3;
                T3ProVFactor = 0.7;
                T3ProChaosSmoothingEnabled = true;
                T3ProChaosSmoothingPeriod = 5;
                T3ProFilterEnabled = true;
                T3ProFilterMultiplier = 4.0;
                
                VIDYAPeriod = 9;
                VIDYAVolatilityPeriod = 9;
                VIDYASmoothingEnabled = true;
                VIDYASmoothingPeriod = 5;
                VIDYAFilterEnabled = true;
                VIDYAFilterMultiplier = 4.0;
                
                EasyTrendPeriod = 30;
                EasyTrendSmoothingEnabled = true;
                EasyTrendSmoothingPeriod = 7;
                EasyTrendFilterEnabled = true;
                EasyTrendFilterMultiplier = 0.5;
                EasyTrendATRPeriod = 100;
                
                RubyRiverMAPeriod = 20;
                RubyRiverSmoothingEnabled = true;
                RubyRiverSmoothingPeriod = 5;
                RubyRiverOffsetMultiplier = 0.15;
                RubyRiverOffsetPeriod = 100;
                
                DragonTrendPeriod = 10;
                DragonTrendSmoothingEnabled = true;
                DragonTrendSmoothingPeriod = 5;
                
                SolarWaveATRPeriod = 100;
                SolarWaveTrendMultiplier = 2;
                SolarWaveStopMultiplier = 4;
                
                EnableSoundAlert = true;
            }
            else if (State == State.DataLoaded)
            {
                chartSessionId = DateTime.Now.ToString("HHmmss") + "_" + new Random().Next(1000, 9999);
                InitializeLogFile();
                t3ProEquivalent = T3ProEquivalent(T3ProMAType.EMA, T3ProPeriod, T3ProTCount, T3ProVFactor,
                    T3ProChaosSmoothingEnabled, T3ProMAType.DEMA, T3ProChaosSmoothingPeriod,
                    T3ProFilterEnabled, T3ProFilterMultiplier, 14, true, false, "╬ô├╗Γûô", "╬ô├╗Γò¥", 10);
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
                LogAlways($"ActiveNikiTrader | Min={MinIndicatorsRequired}/6 | DT_AFTER_RR={EnableDTAfterRR}");
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
                indicatorsReady = true; 
                return; 
            }
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var ind in ChartControl.Indicators)
            {
                var t = ind.GetType();
                switch (t.Name)
                {
                    case "ninZaRubyRiver": rubyRiver = ind; rrIsUptrend = t.GetField("isUptrend", flags); break;
                    case "ninZaVIDYAPro": vidyaPro = ind; vyIsUptrend = t.GetField("isUptrend", flags); break;
                    case "ninZaEasyTrend": easyTrend = ind; etIsUptrend = t.GetField("isUptrend", flags); break;
                    case "ninZaDragonTrend": dragonTrend = ind; dtPrevSignal = t.GetField("prevSignal", flags); break;
                    case "ninZaSolarWave": solarWave = ind; swIsUptrend = t.GetField("isUptrend", flags); swCountWave = t.GetField("countWave", flags); break;
                    case "ninZaT3Pro": ninZaT3Pro = ind; t3pIsUptrend = t.GetField("isUptrend", flags); break;
                }
            }
            useHostedT3Pro = ninZaT3Pro == null;
            useHostedVIDYAPro = vidyaPro == null;
            useHostedEasyTrend = easyTrend == null;
            useHostedRubyRiver = rubyRiver == null;
            useHostedDragonTrend = dragonTrend == null;
            useHostedSolarWave = solarWave == null;
            indicatorsReady = true;
        }
        
        private void LogDetectedIndicators()
        {
            LogAlways($"--- Indicators Detected on Chart ---");
            
            // Log ninZa indicators found
            LogAlways($"  ninZaRubyRiver:   {(rubyRiver != null ? "FOUND" : "not found")}");
            LogAlways($"  ninZaDragonTrend: {(dragonTrend != null ? "FOUND" : "not found")}");
            LogAlways($"  ninZaVIDYAPro:    {(vidyaPro != null ? "FOUND" : "not found")}");
            LogAlways($"  ninZaEasyTrend:   {(easyTrend != null ? "FOUND" : "not found")}");
            LogAlways($"  ninZaSolarWave:   {(solarWave != null ? "FOUND" : "not found")}");
            LogAlways($"  ninZaT3Pro:       {(ninZaT3Pro != null ? "FOUND" : "not found")}");
            
            // Log which equivalents are being used
            LogAlways($"--- Equivalent Indicators ---");
            LogAlways($"  T3ProEquivalent:       {(useHostedT3Pro ? "ACTIVE (using hosted)" : "inactive (using ninZa)")}");
            LogAlways($"  VIDYAProEquivalent:    {(useHostedVIDYAPro ? "ACTIVE (using hosted)" : "inactive (using ninZa)")}");
            LogAlways($"  EasyTrendEquivalent:   {(useHostedEasyTrend ? "ACTIVE (using hosted)" : "inactive (using ninZa)")}");
            LogAlways($"  RubyRiverEquivalent:   {(useHostedRubyRiver ? "ACTIVE (using hosted)" : "inactive (using ninZa)")}");
            LogAlways($"  DragonTrendEquivalent: {(useHostedDragonTrend ? "ACTIVE (using hosted)" : "inactive (using ninZa)")}");
            LogAlways($"  SolarWaveEquivalent:   {(useHostedSolarWave ? "ACTIVE (using hosted)" : "inactive (using ninZa)")}");
            
            // List all indicators on chart
            if (ChartControl?.Indicators != null)
            {
                LogAlways($"--- All Chart Indicators ({ChartControl.Indicators.Count}) ---");
                foreach (var ind in ChartControl.Indicators)
                {
                    LogAlways($"  - {ind.GetType().Name}");
                }
            }
            LogAlways($"--------------------------------");
        }
        
        private void LogAlways(string msg)
        {
            Print(msg);
            if (logWriter != null)
                try { logWriter.WriteLine($"{DateTime.Now:HH:mm:ss} | {msg}"); } catch { }
        }
        
        private bool GetBool(object o, FieldInfo f) { try { return o != null && f != null && (bool)f.GetValue(o); } catch { return false; } }
        private double GetDbl(object o, FieldInfo f) { try { return o != null && f != null ? (double)f.GetValue(o) : 0; } catch { return 0; } }
        private int GetInt(object o, FieldInfo f) { try { return o != null && f != null ? (int)f.GetValue(o) : 0; } catch { return 0; } }
        
        public bool RR_IsUp => useHostedRubyRiver ? (rubyRiverEquivalent?.IsUptrend ?? false) : GetBool(rubyRiver, rrIsUptrend);
        public bool VY_IsUp => useHostedVIDYAPro ? (vidyaProEquivalent?.IsUptrend ?? false) : GetBool(vidyaPro, vyIsUptrend);
        public bool ET_IsUp => useHostedEasyTrend ? (easyTrendEquivalent?.IsUptrend ?? false) : GetBool(easyTrend, etIsUptrend);
        public double DT_Signal => useHostedDragonTrend ? (dragonTrendEquivalent?.PrevSignal ?? 0) : GetDbl(dragonTrend, dtPrevSignal);
        public bool DT_IsUp => DT_Signal > 0;
        public bool DT_IsDown => DT_Signal < 0;
        public bool SW_IsUp => useHostedSolarWave ? (solarWaveEquivalent?.IsUptrend ?? false) : GetBool(solarWave, swIsUptrend);
        public int SW_Count => useHostedSolarWave ? (solarWaveEquivalent?.CountWave ?? 0) : GetInt(solarWave, swCountWave);
        public bool T3P_IsUp => useHostedT3Pro ? (t3ProEquivalent?.IsUptrend ?? false) : GetBool(ninZaT3Pro, t3pIsUptrend);
        
        private int GetEnabledCount() => (UseRubyRiver?1:0)+(UseDragonTrend?1:0)+(UseSolarWave?1:0)+(UseVIDYAPro?1:0)+(UseEasyTrend?1:0)+(UseT3Pro?1:0);
        
        private int GetEffectiveMinimum()
        {
            int enabled = GetEnabledCount();
            if (enabled == 0) return 0;
            if (enabled <= 4) return enabled;  // 1-4 enabled: require ALL
            return MinIndicatorsRequired;       // 5-6 enabled: use parameter (default 4)
        }
        
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
        
        private bool PassesStabilityCheck(DateTime t, bool isRR, bool isDT)
        {
            double secRR = (t - lastRR_FlipTime).TotalSeconds, secDT = (t - lastDT_FlipTime).TotalSeconds;
            if (isRR && lastDT_FlipTime != DateTime.MinValue && secDT < MinSecondsSinceFlip) return false;
            if (isDT && (secRR < 10 || secRR > 180)) return false;
            return true;
        }
        
        private bool PassesChoppyFilter() => recentFlips.Count <= MaxFlipsPerMinute;
        
        private void RecordFlip(DateTime t)
        {
            recentFlips.Enqueue(t);
            while (recentFlips.Count > 0 && (t - recentFlips.Peek()).TotalSeconds > 60) recentFlips.Dequeue();
        }

        #region Chart Panel
        private void CreateControlPanel()
        {
            try
            {
                // Initialize panel settings file path
                string settingsDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NinjaTrader 8", "settings");
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
                    Background = new SolidColorBrush(Color.FromArgb(230, 30, 30, 40)),
                    MinWidth = 200,
                    RenderTransform = transformGroup,
                    RenderTransformOrigin = new Point(0, 1),  // Scale from bottom-left
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
                stack.Children.Add(new TextBlock
                {
                    Text = "ActiveNiki Trader",
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Cyan,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 2)
                });
                lblSubtitle = new TextBlock { Foreground = Brushes.LightGray, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,6) };
                stack.Children.Add(lblSubtitle);
                
                stack.Children.Add(CreateRow("Ruby River", ref chkRubyRiver, ref lblRubyRiver, UseRubyRiver));
                stack.Children.Add(CreateRow("Dragon Trend", ref chkDragonTrend, ref lblDragonTrend, UseDragonTrend));
                stack.Children.Add(CreateRow("VIDYA Pro", ref chkVIDYA, ref lblVIDYA, UseVIDYAPro));
                stack.Children.Add(CreateRow("Easy Trend", ref chkEasyTrend, ref lblEasyTrend, UseEasyTrend));
                stack.Children.Add(CreateRow("Solar Wave", ref chkSolarWave, ref lblSolarWave, UseSolarWave));
                stack.Children.Add(CreateRow("T3 Pro", ref chkT3Pro, ref lblT3Pro, UseT3Pro));
                
                stack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(0,6,0,6) });

                lblTriggerMode = new TextBlock { Text = "Triggers: RR + DT", Foreground = Brushes.LightGray, FontSize = 9 };
                lblTradeStatus = new TextBlock { Text = "Mode: TRADER", Foreground = Brushes.Cyan, FontWeight = FontWeights.Bold, FontSize = 10, Margin = new Thickness(0,2,0,2) };
                lblSessionStats = new TextBlock { Text = "Signals: 0", Foreground = Brushes.LightGray, FontSize = 9 };

                stack.Children.Add(lblTriggerMode);
                stack.Children.Add(lblTradeStatus);
                stack.Children.Add(lblSessionStats);
                
                stack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(0,6,0,6) });
                
                signalBorder = new Border { BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(3), Padding = new Thickness(4) };
                lblLastSignal = new TextBlock { Text = "Last Signal: None", Foreground = Brushes.Gray, FontSize = 9, TextWrapping = TextWrapping.Wrap };
                signalBorder.Child = lblLastSignal;
                stack.Children.Add(signalBorder);
                
                border.Child = stack;
                controlPanel.Children.Add(border);

                // Add resize grip at bottom-right corner
                resizeGrip = new Border
                {
                    Width = 16,
                    Height = 16,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Background = Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.SizeNWSE,
                    Margin = new Thickness(0, 0, 2, 2)
                };

                // Draw resize grip lines
                var gripCanvas = new Canvas { Width = 12, Height = 12 };
                for (int i = 0; i < 3; i++)
                {
                    var line = new Line
                    {
                        X1 = 10 - i * 4, Y1 = 10,
                        X2 = 10, Y2 = 10 - i * 4,
                        Stroke = new SolidColorBrush(Color.FromRgb(120, 120, 140)),
                        StrokeThickness = 1
                    };
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
            if (isDragging)
            {
                isDragging = false;
                controlPanel.ReleaseMouseCapture();
                SavePanelSettings();
                e.Handled = true;
            }
        }

        private void Panel_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (isDragging)
            {
                Point currentPoint = e.GetPosition(ChartControl?.Parent as UIElement);
                double newX = currentPoint.X - dragStartPoint.X;
                double newY = currentPoint.Y - dragStartPoint.Y;

                // Constrain to chart boundaries
                var parent = ChartControl?.Parent as FrameworkElement;
                if (parent != null && controlPanel != null)
                {
                    double panelWidth = controlPanel.ActualWidth > 0 ? controlPanel.ActualWidth : 200;
                    double panelHeight = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : 300;

                    // Account for the initial margin (10, 0, 0, 30)
                    double minX = -10;  // Can move left to edge
                    double maxX = parent.ActualWidth - panelWidth - 10;
                    double minY = -(parent.ActualHeight - panelHeight - 30);  // Can move up
                    double maxY = 0;  // Bottom edge (due to VerticalAlignment.Bottom)

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
            if (isResizing)
            {
                isResizing = false;
                resizeGrip.ReleaseMouseCapture();
                SavePanelSettings();
                e.Handled = true;
            }
        }

        private void ResizeGrip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (isResizing)
            {
                Point currentPoint = e.GetPosition(ChartControl?.Parent as UIElement);
                double deltaX = currentPoint.X - resizeStartPoint.X;
                double deltaY = currentPoint.Y - resizeStartPoint.Y;

                // Calculate new scale based on drag distance
                // Panel is anchored bottom-left, so dragging right/down should enlarge
                double baseWidth = controlPanel.ActualWidth > 0 ? controlPanel.ActualWidth / panelScale.ScaleX : 200;
                double baseHeight = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight / panelScale.ScaleY : 300;

                // Use average of X and Y deltas for uniform scaling
                double avgDelta = (deltaX - deltaY) / 2;  // Subtract deltaY because down is positive but should shrink
                double newScale = resizeStartWidth + avgDelta / baseWidth;

                // Constrain scale between 0.5 and 2.0
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
                {
                    panelTransform.X = x;
                    panelTransform.Y = y;
                }
                if (parts.Length >= 4 && double.TryParse(parts[2], out double scaleX) && double.TryParse(parts[3], out double scaleY))
                {
                    panelScale.ScaleX = Math.Max(0.5, Math.Min(2.0, scaleX));
                    panelScale.ScaleY = Math.Max(0.5, Math.Min(2.0, scaleY));
                }
            }
            catch { }
        }
        
        private void UpdatePanel()
        {
            if (!panelActive || ChartControl == null) return;
            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                // Update subtitle to reflect current checkbox state
                int enabled = GetEnabledCount();
                int effMin = GetEffectiveMinimum();
                if (lblSubtitle != null)
                {
                    string triggers = EnableDTAfterRR ? " + DT_AFTER_RR" : "";
                    if (enabled == 0)
                        lblSubtitle.Text = "No indicators";
                    else
                        lblSubtitle.Text = $"{effMin}/{enabled}{triggers}";
                }

                UpdLbl(lblRubyRiver, RR_IsUp, UseRubyRiver);
                UpdLbl(lblDragonTrend, DT_IsUp, UseDragonTrend);
                UpdLbl(lblSolarWave, SW_IsUp, UseSolarWave);
                UpdLbl(lblVIDYA, VY_IsUp, UseVIDYAPro);
                UpdLbl(lblEasyTrend, ET_IsUp, UseEasyTrend);
                UpdLbl(lblT3Pro, T3P_IsUp, UseT3Pro);

                var (bull, bear, total) = GetConfluence();
                int aligned = Math.Max(bull, bear);
                string dir = bull > bear ? "LONG" : bear > bull ? "SHORT" : "---";

                if (lblTriggerMode != null) lblTriggerMode.Text = EnableDTAfterRR ? "Triggers: RR + DT" : "Triggers: RR only";
                if (lblSessionStats != null) lblSessionStats.Text = $"Signals: {signalCount}";

                // Update signal box based on CURRENT live confluence
                if (lblLastSignal != null && signalBorder != null)
                {
                    if (total == 0)
                    {
                        lblLastSignal.Text = "No indicators selected";
                        lblLastSignal.Foreground = Brushes.Gray;
                        lblLastSignal.FontWeight = FontWeights.Normal;
                        signalBorder.BorderBrush = Brushes.Transparent;
                        signalBorder.Background = Brushes.Transparent;
                    }
                    else if (aligned >= effMin)
                    {
                        // Show current signal based on live confluence
                        lblLastSignal.Text = $"SIGNAL: {dir} @ {aligned}/{total}";
                        lblLastSignal.FontWeight = FontWeights.Bold;
                        lblLastSignal.Foreground = dir == "LONG" ? Brushes.Lime : Brushes.Red;
                        signalBorder.BorderBrush = dir == "LONG" ? Brushes.Lime : Brushes.Red;
                        signalBorder.Background = new SolidColorBrush(dir == "LONG" ? Color.FromArgb(60, 0, 255, 0) : Color.FromArgb(60, 255, 0, 0));
                    }
                    else
                    {
                        // Confluence below threshold - show NEUTRAL
                        lblLastSignal.Text = $"NEUTRAL ({aligned}/{total})";
                        lblLastSignal.FontWeight = FontWeights.Normal;
                        lblLastSignal.Foreground = Brushes.Yellow;
                        signalBorder.BorderBrush = Brushes.Gray;
                        signalBorder.Background = Brushes.Transparent;
                    }
                }
            });
        }
        
        private void UpdateSignalDisplay(string dir, string trigger, int aligned, int total, DateTime t)
        {
            signalCount++;
            lastSignalText = $"SIGNAL: {dir} @ {aligned}/{total}\n[{trigger}] {t:HH:mm:ss}";

            if (EnableSoundAlert)
            {
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
            }

            // Panel update is handled by UpdatePanel() which is called right after
        }
        
        private void UpdLbl(TextBlock l, bool? v, bool en) { if (l == null) return; if (!en) { l.Text = "OFF"; l.Foreground = Brushes.Gray; } else if (!v.HasValue) { l.Text = "MIX"; l.Foreground = Brushes.Yellow; } else { l.Text = v.Value ? "UP" : "DN"; l.Foreground = v.Value ? Brushes.Lime : Brushes.Red; } }
        #endregion

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade || !indicatorsReady) return;
            UpdatePanel();
            
            DateTime barTime = Time[0];
            bool inWindow = barTime.TimeOfDay >= startTime && barTime.TimeOfDay <= endTime;
            
            bool rrFlipUp = UseRubyRiver && RR_IsUp && !prevRR_IsUp && !isFirstBar;
            bool rrFlipDn = UseRubyRiver && !RR_IsUp && prevRR_IsUp && !isFirstBar;
            bool dtFlipUp = UseDragonTrend && DT_IsUp && !prevDT_IsUp && !isFirstBar;
            bool dtFlipDn = UseDragonTrend && DT_IsDown && prevDT_IsUp && !isFirstBar;
            
            if (rrFlipUp || rrFlipDn) { lastRR_FlipTime = barTime; RecordFlip(barTime); PrintAndLog($"RR flip {(rrFlipUp ? "UP" : "DN")} @ {barTime:HH:mm:ss} | {Close[0]:F2}"); }
            if (dtFlipUp || dtFlipDn) { lastDT_FlipTime = barTime; RecordFlip(barTime); PrintAndLog($"DT flip {(dtFlipUp ? "UP" : "DN")} @ {barTime:HH:mm:ss} | {Close[0]:F2}"); }
            
            if (inWindow)
            {
                var (bull, bear, total) = GetConfluence();
                int effMin = GetEffectiveMinimum();
                
                // Skip if no indicators enabled
                if (total == 0) { if (UseRubyRiver) prevRR_IsUp = RR_IsUp; if (UseDragonTrend) prevDT_IsUp = DT_IsUp; isFirstBar = false; return; }
                
                // RR_FLIP trigger
                if (RequireRubyRiverTrigger && (rrFlipUp || rrFlipDn))
                {
                    bool isLong = rrFlipUp;
                    int aligned = isLong ? bull : bear;
                    if (aligned >= effMin && PassesStabilityCheck(barTime, true, false) && PassesChoppyFilter())
                    {
                        string dir = isLong ? "LONG" : "SHORT";
                        LogSignal(dir, "RR_FLIP", barTime, aligned, total);
                        UpdateSignalDisplay(dir, "RR_FLIP", aligned, total, barTime);
                    }
                }

                // DT_AFTER_RR trigger
                if (EnableDTAfterRR && (dtFlipUp || dtFlipDn))
                {
                    bool dtMatchesRR = (dtFlipUp && RR_IsUp) || (dtFlipDn && !RR_IsUp);
                    if (dtMatchesRR)
                    {
                        bool isLong = dtFlipUp;
                        int aligned = isLong ? bull : bear;
                        if (aligned >= effMin && PassesStabilityCheck(barTime, false, true) && PassesChoppyFilter())
                        {
                            string dir = isLong ? "LONG" : "SHORT";
                            LogSignal(dir, "DT_AFTER_RR", barTime, aligned, total);
                            UpdateSignalDisplay(dir, "DT_AFTER_RR", aligned, total, barTime);
                        }
                    }
                }
            }
            
            if (UseRubyRiver) prevRR_IsUp = RR_IsUp;
            if (UseDragonTrend) prevDT_IsUp = DT_IsUp;
            isFirstBar = false;
        }
        
        private void LogSignal(string dir, string trigger, DateTime t, int aligned, int total)
        {
            PrintAndLog($"");
            PrintAndLog($"*** SIGNAL: {dir} @ {t:HH:mm:ss} [{trigger}] ***");
            PrintAndLog($"    Price: {Close[0]:F2} | Confluence: {aligned}/{total}");
            PrintAndLog($"    RR={Ts(RR_IsUp)} DT={DT_Signal:F0} VY={Ts(VY_IsUp)} ET={Ts(ET_IsUp)} SW={SW_Count} T3P={Ts(T3P_IsUp)}");
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
                logWriter.WriteLine($"    Min={MinIndicatorsRequired}/6, MaxFlips={MaxFlipsPerMinute}, DT_AFTER_RR={EnableDTAfterRR}\n");
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
            if (logWriter != null && DateTime.Now.TimeOfDay >= startTime && DateTime.Now.TimeOfDay <= endTime)
                try { logWriter.WriteLine($"{DateTime.Now:HH:mm:ss} | {msg}"); } catch { }
        }
        #endregion
    }
}