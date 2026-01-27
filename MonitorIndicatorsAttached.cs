#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// MonitorIndicatorsAttached - Reads live values from 8 indicators (6 ninZa + T3 + T3Pro)
    /// 
    /// CONVERTED FROM Niki1 STRATEGY to INDICATOR to avoid being disabled by discretionary trades.
    /// This indicator ONLY monitors and logs - it cannot and will not place any trades.
    /// 
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// INDICATOR STATUS:
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// 
    /// ✅ RUBY RIVER (ninZa) - FULLY LIVE via reflection
    /// ⚠️ VIDYA PRO (ninZa) - TREND SIGNAL LIVE, value not accessible
    /// ⚠️ EASY TREND (ninZa) - TREND SIGNAL LIVE, value not accessible
    /// ✅ DRAGON TREND (ninZa) - FULLY LIVE via reflection
    /// ✅ SOLAR WAVE (ninZa) - FULLY LIVE via reflection
    /// ✅ AAA+ TREND SYNC (ninZa) - FULLY LIVE via reflection
    /// ✅ T3 (NinjaTrader built-in) - FULLY LIVE via direct hosting
    /// ✅ T3 PRO EQUIVALENT - FULLY LIVE via direct hosting (or reflection from ninZaT3Pro)
    /// 
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// LOGGING: All Print() output also saved to custom log file via PrintAndLog()
    /// Log Location: C:\Users\Administrator\Documents\NinjaTrader 8\log\MonitorIndicatorsAttached_log_YYYY-MM-DD_ChartID.txt
    /// Logging Hours: 6:50 AM - 11:59 AM only
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public class MonitorIndicatorsAttached : Indicator
    {
        // ==================== Indicator References ====================
        // ninZa indicators (read from chart via reflection)
        private object rubyRiver;
        private object vidyaPro;
        private object easyTrend;
        private object dragonTrend;
        private object solarWave;
        private object aaaTrendSync;
        private object ninZaT3Pro;  // Original ninZa T3 Pro (if available)
        
        // Hosted indicators (NinjaTrader built-in + custom)
        private T3 t3Indicator;
        private T3ProEquivalent t3ProEquivalent;
        
        // ==================== Custom Log File ====================
        private string logFilePath;
        private StreamWriter logWriter;
        private string chartSessionId;  // Unique identifier for this chart session
        
        // ==================== T3 Parameters ====================
        [NinjaScriptProperty]
        [Display(Name = "T3 Period", Order = 1, GroupName = "T3 Settings")]
        public int T3Period { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "T3 TCount", Order = 2, GroupName = "T3 Settings")]
        public int T3TCount { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "T3 VFactor", Order = 3, GroupName = "T3 Settings")]
        public double T3VFactor { get; set; }
        
        // ==================== T3 Pro Parameters ====================
        [NinjaScriptProperty]
        [Display(Name = "T3Pro Period", Order = 1, GroupName = "T3 Pro Settings")]
        public int T3ProPeriod { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "T3Pro TCount", Order = 2, GroupName = "T3 Pro Settings")]
        public int T3ProTCount { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "T3Pro VFactor", Order = 3, GroupName = "T3 Pro Settings")]
        public double T3ProVFactor { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Chaos Smoothing Enabled", Order = 4, GroupName = "T3 Pro Settings")]
        public bool T3ProChaosSmoothingEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Chaos Smoothing Period", Order = 5, GroupName = "T3 Pro Settings")]
        public int T3ProChaosSmoothingPeriod { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Filter Enabled", Order = 6, GroupName = "T3 Pro Settings")]
        public bool T3ProFilterEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Filter Multiplier", Order = 7, GroupName = "T3 Pro Settings")]
        public double T3ProFilterMultiplier { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use ninZa T3Pro from Chart", Description = "If checked read T3 Pro from chart indicator. If cleared use custom made T3 Pro Equivalent", Order = 8, GroupName = "T3 Pro Settings")]
        public bool UseNinZaT3ProFromChart { get; set; }
        
        // ==================== Ruby River Fields ====================
        private FieldInfo rrUpperMA;
        private FieldInfo rrLowerMA;
        private FieldInfo rrIsUptrend;
        private FieldInfo rrBackInsideRiver;
        
        // ==================== VIDYA Pro Fields ====================
        private FieldInfo vyIsUptrend;
        
        // ==================== Easy Trend Fields ====================
        private FieldInfo etIsUptrend;
        
        // ==================== Dragon Trend Fields ====================
        private FieldInfo dtPrevSignal;
        
        // ==================== Solar Wave Fields ====================
        private FieldInfo swCurrentLadderRungPrice;
        private FieldInfo swPrevLadderRungPrice;
        private FieldInfo swStopCurrentValue;
        private FieldInfo swIsUptrend;
        private FieldInfo swCountWave;
        
        // ==================== AAA+ Trend Sync Fields ====================
        private FieldInfo aaaFastUptrend;
        private FieldInfo aaaMidUptrend;
        private FieldInfo aaaSlowUptrend;
        
        // ==================== ninZa T3 Pro Fields (reflection) ====================
        private FieldInfo t3pIsUptrend;
        private FieldInfo t3pFilteredValue;
        private PropertyInfo t3pT3ValueProp;
        private PropertyInfo t3pSignalTrendProp;
        
        // ==================== Previous State for Change Detection ====================
        private bool prevRR_IsUptrend;
        private bool prevRR_BackInsideRiver;
        private bool prevVY_IsUptrend;
        private bool prevET_IsUptrend;
        private double prevDT_Signal;
        private bool prevSW_IsUptrend;
        private int prevSW_WaveCount;
        private bool prevAAA_FastUptrend;
        private bool prevAAA_MidUptrend;
        private bool prevAAA_SlowUptrend;
        private double prevT3_Value;
        private bool prevT3_IsRising;
        private double prevT3Pro_Value;
        private bool prevT3Pro_IsUptrend;
        
        private bool isFirstBar = true;
        private bool useHostedT3Pro = false;  // Determined at runtime
        private int barsRequired = 20;
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "MonitorIndicatorsAttached";
                Description = "Monitors 8 indicators (6 ninZa + T3 + T3Pro) - converted from Niki1 strategy to avoid disabling on discretionary trades";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;  // Overlay on price chart (doesn't plot anything visible)
                DisplayInDataBox = false;
                DrawOnPricePanel = false;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = false;  // Keep running even when chart not focused
                
                // T3 default parameters
                T3Period = 14;
                T3TCount = 3;
                T3VFactor = 0.7;
                
                // T3 Pro default parameters (from template)
                T3ProPeriod = 14;
                T3ProTCount = 3;
                T3ProVFactor = 0.7;
                T3ProChaosSmoothingEnabled = true;
                T3ProChaosSmoothingPeriod = 5;
                T3ProFilterEnabled = true;
                T3ProFilterMultiplier = 4.0;
                UseNinZaT3ProFromChart = true;  // Try chart first
            }
            else if (State == State.DataLoaded)
            {
                // Generate unique session ID for this chart instance
                // Format: HHmmss_RandomChars (e.g., "093845_A7B3")
                chartSessionId = GenerateChartSessionId();
                
                // Initialize custom log file
                InitializeLogFile();
                
                // Host T3 indicator directly (NinjaTrader built-in)
                t3Indicator = T3(T3Period, T3TCount, T3VFactor);
                PrintAndLog($"✓ T3 indicator hosted (Period={T3Period}, TCount={T3TCount}, VFactor={T3VFactor})");
                
                // Host T3 Pro Equivalent as fallback
                t3ProEquivalent = T3ProEquivalent(
                    T3ProMAType.EMA,           // MAType
                    T3ProPeriod,               // Period
                    T3ProTCount,               // TCount
                    T3ProVFactor,              // VFactor
                    T3ProChaosSmoothingEnabled,// ChaosSmoothingEnabled
                    T3ProMAType.DEMA,          // ChaosSmoothingMethod
                    T3ProChaosSmoothingPeriod, // ChaosSmoothingPeriod
                    T3ProFilterEnabled,        // FilterEnabled
                    T3ProFilterMultiplier,     // FilterMultiplier
                    14,                        // FilterATRPeriod
                    true,                      // PlotEnabled
                    false,                     // MarkerEnabled
                    "▲ + T3",                  // MarkerStringUptrend
                    "T3 + ▼",                  // MarkerStringDowntrend
                    10                         // MarkerOffset
                );
                PrintAndLog($"✓ T3ProEquivalent hosted as fallback");
            }
            else if (State == State.Historical)
            {
                // Load ninZa indicators from chart - must be done after Historical state
                // when ChartControl.Indicators is populated
                LoadNinZaIndicators();
            }
            else if (State == State.Terminated)
            {
                // Close log file when indicator terminates
                CloseLogFile();
            }
        }
        
        /// <summary>
        /// Generate a unique session ID for this chart instance
        /// Format: HHmmss_XXXX where XXXX is a random alphanumeric string
        /// This ensures each chart session has a distinct log file
        /// </summary>
        private string GenerateChartSessionId()
        {
            // Time component: when this chart session started
            string timeComponent = DateTime.Now.ToString("HHmmss");
            
            // Random component: 4 alphanumeric characters for uniqueness
            string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            Random random = new Random();
            string randomComponent = new string(Enumerable.Range(0, 4).Select(_ => chars[random.Next(chars.Length)]).ToArray());
            
            return $"{timeComponent}_{randomComponent}";
        }
        
        /// <summary>
        /// Initialize the custom log file for this session
        /// </summary>
        private void InitializeLogFile()
        {
            try
            {
                string logDirectory = @"C:\Users\Administrator\Documents\NinjaTrader 8\log";
                
                // Ensure directory exists
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
                
                // Create log file path with today's date AND chart session ID
                // Format: MonitorIndicatorsAttached_log_2025-12-15_093845_A7B3.txt
                logFilePath = Path.Combine(logDirectory, $"MonitorIndicatorsAttached_log_{DateTime.Now:yyyy-MM-dd}_{chartSessionId}.txt");
                
                // Open file in append mode with auto-flush
                logWriter = new StreamWriter(logFilePath, true);
                logWriter.AutoFlush = true;
                
                // Write session header
                logWriter.WriteLine($"");
                logWriter.WriteLine($"═══════════════════════════════════════════════════════════");
                logWriter.WriteLine($"  MonitorIndicatorsAttached Log Session Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                logWriter.WriteLine($"  Chart Session ID: {chartSessionId}");
                logWriter.WriteLine($"  (Indicator version - not affected by discretionary trades)");
                logWriter.WriteLine($"═══════════════════════════════════════════════════════════");
                logWriter.WriteLine($"");
            }
            catch (Exception ex)
            {
                Print($"ERROR: Failed to initialize log file: {ex.Message}");
                logWriter = null;
            }
        }
        
        /// <summary>
        /// Close the log file cleanly
        /// </summary>
        private void CloseLogFile()
        {
            try
            {
                if (logWriter != null)
                {
                    logWriter.WriteLine($"");
                    logWriter.WriteLine($"═══════════════════════════════════════════════════════════");
                    logWriter.WriteLine($"  MonitorIndicatorsAttached Log Session Ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    logWriter.WriteLine($"  Chart Session ID: {chartSessionId}");
                    logWriter.WriteLine($"═══════════════════════════════════════════════════════════");
                    logWriter.WriteLine($"");
                    logWriter.Close();
                    logWriter.Dispose();
                    logWriter = null;
                }
            }
            catch (Exception ex)
            {
                Print($"ERROR: Failed to close log file: {ex.Message}");
            }
        }
        
        private void LoadNinZaIndicators()
        {
            if (ChartControl == null || ChartControl.Indicators == null)
            {
                PrintAndLog("WARNING: No chart control or indicators available");
                useHostedT3Pro = true;
                return;
            }
            
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var publicFlags = BindingFlags.Public | BindingFlags.Instance;
            
            foreach (var indicator in ChartControl.Indicators)
            {
                string typeName = indicator.GetType().Name;
                var type = indicator.GetType();
                
                switch (typeName)
                {
                    case "ninZaRubyRiver":
                        rubyRiver = indicator;
                        rrUpperMA = type.GetField("upperMA", flags);
                        rrLowerMA = type.GetField("lowerMA", flags);
                        rrIsUptrend = type.GetField("isUptrend", flags);
                        rrBackInsideRiver = type.GetField("backInsideRiver", flags);
                        PrintAndLog("✓ Ruby River loaded (FULLY LIVE)");
                        break;
                        
                    case "ninZaVIDYAPro":
                        vidyaPro = indicator;
                        vyIsUptrend = type.GetField("isUptrend", flags);
                        PrintAndLog("✓ VIDYA Pro loaded (TREND LIVE)");
                        break;
                        
                    case "ninZaEasyTrend":
                        easyTrend = indicator;
                        etIsUptrend = type.GetField("isUptrend", flags);
                        PrintAndLog("✓ Easy Trend loaded (TREND LIVE)");
                        break;
                        
                    case "ninZaDragonTrend":
                        dragonTrend = indicator;
                        dtPrevSignal = type.GetField("prevSignal", flags);
                        PrintAndLog("✓ Dragon Trend loaded (FULLY LIVE)");
                        break;
                        
                    case "ninZaSolarWave":
                        solarWave = indicator;
                        swCurrentLadderRungPrice = type.GetField("currentLadderRungPrice", flags);
                        swPrevLadderRungPrice = type.GetField("prevLadderRungPrice", flags);
                        swStopCurrentValue = type.GetField("stopCurrentValue", flags);
                        swIsUptrend = type.GetField("isUptrend", flags);
                        swCountWave = type.GetField("countWave", flags);
                        PrintAndLog("✓ Solar Wave loaded (FULLY LIVE)");
                        break;
                        
                    case "ninZaAAATrendSync":
                        aaaTrendSync = indicator;
                        aaaFastUptrend = type.GetField("fastUptrend", flags);
                        aaaMidUptrend = type.GetField("midUptrend", flags);
                        aaaSlowUptrend = type.GetField("slowUptrend", flags);
                        PrintAndLog("✓ AAA+ Trend Sync loaded (FULLY LIVE)");
                        break;
                        
                    case "ninZaT3Pro":
                        ninZaT3Pro = indicator;
                        // Try to get fields via reflection
                        t3pIsUptrend = type.GetField("isUptrend", flags);
                        t3pFilteredValue = type.GetField("filteredValue", flags);
                        // Also try public properties
                        t3pT3ValueProp = type.GetProperty("T3Value", publicFlags) ?? type.GetProperty("T3", publicFlags);
                        t3pSignalTrendProp = type.GetProperty("Signal_Trend", publicFlags);
                        PrintAndLog("✓ ninZa T3 Pro loaded from chart (FULLY LIVE)");
                        useHostedT3Pro = false;
                        break;
                }
            }
            
            // Determine T3Pro source
            if (UseNinZaT3ProFromChart && ninZaT3Pro != null)
            {
                useHostedT3Pro = false;
                PrintAndLog("  → Using ninZa T3 Pro from chart");
            }
            else
            {
                useHostedT3Pro = true;
                PrintAndLog("  → Using hosted T3ProEquivalent (ninZa T3 Pro not found or disabled)");
            }
            
            if (rubyRiver == null) PrintAndLog("⚠ Ruby River NOT found on chart");
            if (vidyaPro == null) PrintAndLog("⚠ VIDYA Pro NOT found on chart");
            if (easyTrend == null) PrintAndLog("⚠ Easy Trend NOT found on chart");
            if (dragonTrend == null) PrintAndLog("⚠ Dragon Trend NOT found on chart");
            if (solarWave == null) PrintAndLog("⚠ Solar Wave NOT found on chart");
            if (aaaTrendSync == null) PrintAndLog("⚠ AAA+ Trend Sync NOT found on chart");
            if (ninZaT3Pro == null) PrintAndLog("⚠ ninZa T3 Pro NOT found on chart (using T3ProEquivalent)");
            
            PrintAndLog("=== All indicators loaded - MonitorIndicatorsAttached READY ===");
        }
        
        // ==================== Helper Methods ====================
        
        /// <summary>
        /// Outputs message to Output window (Print) always.
        /// Only logs to file during trading hours (6:50 AM - 11:59 AM).
        /// Log file location: C:\Users\Administrator\Documents\NinjaTrader 8\log\MonitorIndicatorsAttached_log_YYYY-MM-DD_ChartID.txt
        /// </summary>
        private void PrintAndLog(string message)
        {
            // Always print to Output window
            Print(message);
            
            // Only log to file during 6:50 AM - 11:59 AM
            DateTime now = DateTime.Now;
            TimeSpan currentTime = now.TimeOfDay;
            TimeSpan startTime = new TimeSpan(6, 50, 0);   // 6:50 AM
            TimeSpan endTime = new TimeSpan(11, 59, 0);    // 11:59 AM
            
            if (currentTime >= startTime && currentTime <= endTime)
            {
                try
                {
                    if (logWriter != null)
                    {
                        logWriter.WriteLine($"{now:HH:mm:ss} | {message}");
                    }
                }
                catch (Exception ex)
                {
                    Print($"ERROR: Failed to write to log file: {ex.Message}");
                }
            }
        }
        
        private double GetDouble(object obj, FieldInfo field)
        {
            if (obj == null || field == null) return double.NaN;
            try { return (double)field.GetValue(obj); }
            catch { return double.NaN; }
        }
        
        private bool GetBool(object obj, FieldInfo field)
        {
            if (obj == null || field == null) return false;
            try { return (bool)field.GetValue(obj); }
            catch { return false; }
        }
        
        private int GetInt(object obj, FieldInfo field)
        {
            if (obj == null || field == null) return 0;
            try { return (int)field.GetValue(obj); }
            catch { return 0; }
        }
        
        private double GetSeriesValue(object obj, PropertyInfo prop, int barsAgo = 0)
        {
            if (obj == null || prop == null) return double.NaN;
            try
            {
                var series = prop.GetValue(obj) as ISeries<double>;
                if (series != null && series.Count > barsAgo)
                    return series.GetValueAt(series.Count - 1 - barsAgo);
                return double.NaN;
            }
            catch { return double.NaN; }
        }
        
        private string TrendStr(bool isUptrend) => isUptrend ? "UP" : "DN";
        private string TrendStr(double signal) => signal > 0 ? "UP" : (signal < 0 ? "DN" : "FLAT");
        
        // ==================== Public Indicator Value Properties ====================
        
        // Ruby River (FULLY LIVE)
        public double RR_UpperMA => GetDouble(rubyRiver, rrUpperMA);
        public double RR_LowerMA => GetDouble(rubyRiver, rrLowerMA);
        public bool RR_IsUptrend => GetBool(rubyRiver, rrIsUptrend);
        public bool RR_BackInsideRiver => GetBool(rubyRiver, rrBackInsideRiver);
        
        // VIDYA Pro (TREND LIVE)
        public bool VY_IsUptrend => GetBool(vidyaPro, vyIsUptrend);
        
        // Easy Trend (TREND LIVE)
        public bool ET_IsUptrend => GetBool(easyTrend, etIsUptrend);
        
        // Dragon Trend (FULLY LIVE)
        public double DT_Signal => GetDouble(dragonTrend, dtPrevSignal);
        public bool DT_IsUptrend => DT_Signal > 0;
        
        // Solar Wave (FULLY LIVE)
        public double SW_CurrentPrice => GetDouble(solarWave, swCurrentLadderRungPrice);
        public double SW_PrevPrice => GetDouble(solarWave, swPrevLadderRungPrice);
        public double SW_StopValue => GetDouble(solarWave, swStopCurrentValue);
        public bool SW_IsUptrend => GetBool(solarWave, swIsUptrend);
        public int SW_WaveCount => GetInt(solarWave, swCountWave);
        
        // AAA+ Trend Sync (FULLY LIVE)
        public bool AAA_FastUptrend => GetBool(aaaTrendSync, aaaFastUptrend);
        public bool AAA_MidUptrend => GetBool(aaaTrendSync, aaaMidUptrend);
        public bool AAA_SlowUptrend => GetBool(aaaTrendSync, aaaSlowUptrend);
        public bool AAA_AllSynced => AAA_FastUptrend == AAA_MidUptrend && AAA_MidUptrend == AAA_SlowUptrend;
        public bool AAA_AllUp => AAA_FastUptrend && AAA_MidUptrend && AAA_SlowUptrend;
        public bool AAA_AllDown => !AAA_FastUptrend && !AAA_MidUptrend && !AAA_SlowUptrend;
        
        // T3 (FULLY LIVE - hosted directly)
        public double T3_Value => t3Indicator != null && CurrentBar > 0 ? t3Indicator[0] : double.NaN;
        public double T3_PrevValue => t3Indicator != null && CurrentBar > 1 ? t3Indicator[1] : double.NaN;
        public bool T3_IsRising => T3_Value > T3_PrevValue;
        public bool T3_IsFalling => T3_Value < T3_PrevValue;
        public bool T3_PriceAbove => Close[0] > T3_Value;
        public bool T3_PriceBelow => Close[0] < T3_Value;
        
        // T3 Pro (FULLY LIVE - from chart ninZaT3Pro or hosted T3ProEquivalent)
        public double T3Pro_Value
        {
            get
            {
                if (useHostedT3Pro)
                {
                    return t3ProEquivalent != null && CurrentBar > 0 ? t3ProEquivalent.T3Value[0] : double.NaN;
                }
                else
                {
                    // Try to read from ninZaT3Pro via reflection
                    double val = GetSeriesValue(ninZaT3Pro, t3pT3ValueProp, 0);
                    if (!double.IsNaN(val)) return val;
                    // Fallback to filtered value field
                    return GetDouble(ninZaT3Pro, t3pFilteredValue);
                }
            }
        }
        
        public double T3Pro_PrevValue
        {
            get
            {
                if (useHostedT3Pro)
                {
                    return t3ProEquivalent != null && CurrentBar > 1 ? t3ProEquivalent.T3Value[1] : double.NaN;
                }
                else
                {
                    return GetSeriesValue(ninZaT3Pro, t3pT3ValueProp, 1);
                }
            }
        }
        
        public bool T3Pro_IsUptrend
        {
            get
            {
                if (useHostedT3Pro)
                {
                    return t3ProEquivalent != null ? t3ProEquivalent.IsUptrend : false;
                }
                else
                {
                    return GetBool(ninZaT3Pro, t3pIsUptrend);
                }
            }
        }
        
        public double T3Pro_Signal
        {
            get
            {
                if (useHostedT3Pro)
                {
                    return t3ProEquivalent != null && CurrentBar > 0 ? t3ProEquivalent.Signal_Trend[0] : 0;
                }
                else
                {
                    return GetSeriesValue(ninZaT3Pro, t3pSignalTrendProp, 0);
                }
            }
        }
        
        public bool T3Pro_IsRising => T3Pro_Value > T3Pro_PrevValue;
        public bool T3Pro_IsFalling => T3Pro_Value < T3Pro_PrevValue;
        public bool T3Pro_PriceAbove => Close[0] > T3Pro_Value;
        public bool T3Pro_PriceBelow => Close[0] < T3Pro_Value;
        
        // ==================== Signal Change Detection ====================
        
        private void DetectAndLogSignalChanges()
        {
            string timeStr = Time[0].ToString("HH:mm");
            
            // Dragon Trend signal change (prominent display)
            double currentDT = DT_Signal;
            if (!isFirstBar && currentDT != prevDT_Signal)
            {
                string direction = currentDT > 0 ? "▲ LONG (UP)" : (currentDT < 0 ? "▼ SHORT (DN)" : "○ FLAT");
                PrintAndLog($"");
                PrintAndLog($"═══════════════════════════════════════════════════════════");
                PrintAndLog($"  🐉 DRAGON TREND SIGNAL CHANGE @ {timeStr} (Bar {CurrentBar})");
                PrintAndLog($"     Signal: {prevDT_Signal:F0} → {currentDT:F0}  [{direction}]");
                PrintAndLog($"     Close: {Close[0]:F2}");
                PrintAndLog($"═══════════════════════════════════════════════════════════");
                PrintAndLog($"");
            }
            prevDT_Signal = currentDT;
            
            // Ruby River trend change
            bool currentRR = RR_IsUptrend;
            if (!isFirstBar && currentRR != prevRR_IsUptrend)
            {
                PrintAndLog($"  ◆ RUBY RIVER trend: {TrendStr(prevRR_IsUptrend)} → {TrendStr(currentRR)} @ {timeStr} | Close={Close[0]:F2}");
            }
            prevRR_IsUptrend = currentRR;
            
            // Ruby River pullback
            bool currentRRInside = RR_BackInsideRiver;
            if (!isFirstBar && currentRRInside != prevRR_BackInsideRiver)
            {
                string status = currentRRInside ? "PULLBACK (inside)" : "OUTSIDE";
                PrintAndLog($"  ◇ RUBY RIVER: {status} @ {timeStr} | Close={Close[0]:F2}");
            }
            prevRR_BackInsideRiver = currentRRInside;
            
            // VIDYA Pro trend change
            bool currentVY = VY_IsUptrend;
            if (!isFirstBar && currentVY != prevVY_IsUptrend)
            {
                PrintAndLog($"  ◆ VIDYA PRO trend: {TrendStr(prevVY_IsUptrend)} → {TrendStr(currentVY)} @ {timeStr} | Close={Close[0]:F2}");
            }
            prevVY_IsUptrend = currentVY;
            
            // Easy Trend change
            bool currentET = ET_IsUptrend;
            if (!isFirstBar && currentET != prevET_IsUptrend)
            {
                PrintAndLog($"  ◆ EASY TREND: {TrendStr(prevET_IsUptrend)} → {TrendStr(currentET)} @ {timeStr} | Close={Close[0]:F2}");
            }
            prevET_IsUptrend = currentET;
            
            // Solar Wave trend change
            bool currentSW = SW_IsUptrend;
            if (!isFirstBar && currentSW != prevSW_IsUptrend)
            {
                PrintAndLog($"  ◆ SOLAR WAVE trend: {TrendStr(prevSW_IsUptrend)} → {TrendStr(currentSW)} @ {timeStr} | Close={Close[0]:F2}");
            }
            prevSW_IsUptrend = currentSW;
            
            // Solar Wave wave count change
            int currentSWWave = SW_WaveCount;
            if (!isFirstBar && currentSWWave != prevSW_WaveCount)
            {
                PrintAndLog($"  ◇ SOLAR WAVE count: {prevSW_WaveCount} → {currentSWWave} @ {timeStr}");
            }
            prevSW_WaveCount = currentSWWave;
            
            // AAA+ trend changes
            bool currentAAAFast = AAA_FastUptrend;
            if (!isFirstBar && currentAAAFast != prevAAA_FastUptrend)
            {
                PrintAndLog($"  ◆ AAA+ FAST: {TrendStr(prevAAA_FastUptrend)} → {TrendStr(currentAAAFast)} @ {timeStr}");
            }
            prevAAA_FastUptrend = currentAAAFast;
            
            bool currentAAAMid = AAA_MidUptrend;
            if (!isFirstBar && currentAAAMid != prevAAA_MidUptrend)
            {
                PrintAndLog($"  ◆ AAA+ MID: {TrendStr(prevAAA_MidUptrend)} → {TrendStr(currentAAAMid)} @ {timeStr}");
            }
            prevAAA_MidUptrend = currentAAAMid;
            
            bool currentAAASlow = AAA_SlowUptrend;
            if (!isFirstBar && currentAAASlow != prevAAA_SlowUptrend)
            {
                PrintAndLog($"  ◆ AAA+ SLOW: {TrendStr(prevAAA_SlowUptrend)} → {TrendStr(currentAAASlow)} @ {timeStr}");
            }
            prevAAA_SlowUptrend = currentAAASlow;
            
            // T3 direction change
            bool currentT3Rising = T3_IsRising;
            if (!isFirstBar && currentT3Rising != prevT3_IsRising)
            {
                string direction = currentT3Rising ? "RISING ↗" : "FALLING ↘";
                PrintAndLog($"  ◆ T3: {direction} @ {timeStr} | T3={T3_Value:F2} | Close={Close[0]:F2}");
            }
            prevT3_IsRising = currentT3Rising;
            prevT3_Value = T3_Value;
            
            // T3 Pro trend change
            bool currentT3ProUptrend = T3Pro_IsUptrend;
            if (!isFirstBar && currentT3ProUptrend != prevT3Pro_IsUptrend)
            {
                string direction = currentT3ProUptrend ? "▲ UPTREND" : "▼ DOWNTREND";
                string source = useHostedT3Pro ? "T3ProEq" : "ninZaT3Pro";
                PrintAndLog($"");
                PrintAndLog($"═══════════════════════════════════════════════════════════");
                PrintAndLog($"  📈 T3 PRO TREND CHANGE ({source}) @ {timeStr} (Bar {CurrentBar})");
                PrintAndLog($"     Trend: {TrendStr(prevT3Pro_IsUptrend)} → {TrendStr(currentT3ProUptrend)}  [{direction}]");
                PrintAndLog($"     T3Pro={T3Pro_Value:F2} | Close={Close[0]:F2}");
                PrintAndLog($"═══════════════════════════════════════════════════════════");
                PrintAndLog($"");
            }
            prevT3Pro_IsUptrend = currentT3ProUptrend;
            prevT3Pro_Value = T3Pro_Value;
            
            isFirstBar = false;
        }
        
        protected override void OnBarUpdate()
        {
            if (CurrentBar < barsRequired)
                return;
            
            // Always detect signal changes
            DetectAndLogSignalChanges();
            
            int totalBars = Bars.Count;
            int barsFromEnd = totalBars - CurrentBar - 1;
            bool shouldPrint = (State == State.Realtime) || (barsFromEnd < 15);
            
            if (shouldPrint)
            {
                double askPrice = GetCurrentAsk();
                double bidPrice = GetCurrentBid();
                string stateInfo = (CurrentBar % 5 == 0) ? $" [{State}]" : "";
                string timeStr = Time[0].ToString("HH:mm");
                
                // AAA sync status
                string aaaSync = AAA_AllUp ? "ALL-UP" : (AAA_AllDown ? "ALL-DN" : "MIXED");
                
                // T3 status
                string t3Direction = T3_IsRising ? "↗" : "↘";
                string t3Position = T3_PriceAbove ? "above" : "below";
                
                // T3 Pro status
                string t3ProDirection = T3Pro_IsUptrend ? "UP" : "DN";
                string t3ProPosition = T3Pro_PriceAbove ? "above" : "below";
                string t3ProSource = useHostedT3Pro ? "Eq" : "ninZa";
                
                // Build output with each indicator on its own line
                PrintAndLog($"");
                PrintAndLog($"Bar {CurrentBar} @ {timeStr} | Close={Close[0]:F2} | Ask={askPrice:F2} Bid={bidPrice:F2}{stateInfo}");
                PrintAndLog($"  RR:  {TrendStr(RR_IsUptrend),-4} | upper={RR_UpperMA:F2} lower={RR_LowerMA:F2} inside={RR_BackInsideRiver}");
                PrintAndLog($"  VY:  {TrendStr(VY_IsUptrend),-4} | (trend signal live)");
                PrintAndLog($"  ET:  {TrendStr(ET_IsUptrend),-4} | (trend signal live)");
                PrintAndLog($"  DT:  {TrendStr(DT_Signal),-4} | signal={DT_Signal:F0}");
                PrintAndLog($"  SW:  {TrendStr(SW_IsUptrend),-4} | curr={SW_CurrentPrice:F2} stop={SW_StopValue:F2} wave={SW_WaveCount}");
                PrintAndLog($"  AAA: {aaaSync,-6} | fast={TrendStr(AAA_FastUptrend)} mid={TrendStr(AAA_MidUptrend)} slow={TrendStr(AAA_SlowUptrend)}");
                PrintAndLog($"  T3:  {t3Direction,-4} | value={T3_Value:F2} | price {t3Position} T3");
                PrintAndLog($"  T3P: {t3ProDirection,-4} | value={T3Pro_Value:F2} | price {t3ProPosition} T3Pro [{t3ProSource}]");
            }
            else if (CurrentBar == barsRequired)
            {
                PrintAndLog($"");
                PrintAndLog($"═══════════════════════════════════════════════════════════");
                PrintAndLog($"  MonitorIndicatorsAttached Active - Monitoring 8 indicators");
                PrintAndLog($"  (6 ninZa + T3 + T3Pro)");
                PrintAndLog($"  Chart Session ID: {chartSessionId}");
                PrintAndLog($"  T3Pro Source: {(useHostedT3Pro ? "T3ProEquivalent (hosted)" : "ninZaT3Pro (chart)")}");
                PrintAndLog($"  Signal changes will be logged as they occur");
                PrintAndLog($"  File logging: 6:50 AM - 11:59 AM only");
                PrintAndLog($"  Log file: {logFilePath}");
                PrintAndLog($"  *** INDICATOR VERSION - Will NOT be disabled by discretionary trades ***");
                PrintAndLog($"═══════════════════════════════════════════════════════════");
                PrintAndLog($"");
            }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MonitorIndicatorsAttached[] cacheMonitorIndicatorsAttached;
		public MonitorIndicatorsAttached MonitorIndicatorsAttached(int t3Period, int t3TCount, double t3VFactor, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, bool t3ProChaosSmoothingEnabled, int t3ProChaosSmoothingPeriod, bool t3ProFilterEnabled, double t3ProFilterMultiplier, bool useNinZaT3ProFromChart)
		{
			return MonitorIndicatorsAttached(Input, t3Period, t3TCount, t3VFactor, t3ProPeriod, t3ProTCount, t3ProVFactor, t3ProChaosSmoothingEnabled, t3ProChaosSmoothingPeriod, t3ProFilterEnabled, t3ProFilterMultiplier, useNinZaT3ProFromChart);
		}

		public MonitorIndicatorsAttached MonitorIndicatorsAttached(ISeries<double> input, int t3Period, int t3TCount, double t3VFactor, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, bool t3ProChaosSmoothingEnabled, int t3ProChaosSmoothingPeriod, bool t3ProFilterEnabled, double t3ProFilterMultiplier, bool useNinZaT3ProFromChart)
		{
			if (cacheMonitorIndicatorsAttached != null)
				for (int idx = 0; idx < cacheMonitorIndicatorsAttached.Length; idx++)
					if (cacheMonitorIndicatorsAttached[idx] != null && cacheMonitorIndicatorsAttached[idx].T3Period == t3Period && cacheMonitorIndicatorsAttached[idx].T3TCount == t3TCount && cacheMonitorIndicatorsAttached[idx].T3VFactor == t3VFactor && cacheMonitorIndicatorsAttached[idx].T3ProPeriod == t3ProPeriod && cacheMonitorIndicatorsAttached[idx].T3ProTCount == t3ProTCount && cacheMonitorIndicatorsAttached[idx].T3ProVFactor == t3ProVFactor && cacheMonitorIndicatorsAttached[idx].T3ProChaosSmoothingEnabled == t3ProChaosSmoothingEnabled && cacheMonitorIndicatorsAttached[idx].T3ProChaosSmoothingPeriod == t3ProChaosSmoothingPeriod && cacheMonitorIndicatorsAttached[idx].T3ProFilterEnabled == t3ProFilterEnabled && cacheMonitorIndicatorsAttached[idx].T3ProFilterMultiplier == t3ProFilterMultiplier && cacheMonitorIndicatorsAttached[idx].UseNinZaT3ProFromChart == useNinZaT3ProFromChart && cacheMonitorIndicatorsAttached[idx].EqualsInput(input))
						return cacheMonitorIndicatorsAttached[idx];
			return CacheIndicator<MonitorIndicatorsAttached>(new MonitorIndicatorsAttached(){ T3Period = t3Period, T3TCount = t3TCount, T3VFactor = t3VFactor, T3ProPeriod = t3ProPeriod, T3ProTCount = t3ProTCount, T3ProVFactor = t3ProVFactor, T3ProChaosSmoothingEnabled = t3ProChaosSmoothingEnabled, T3ProChaosSmoothingPeriod = t3ProChaosSmoothingPeriod, T3ProFilterEnabled = t3ProFilterEnabled, T3ProFilterMultiplier = t3ProFilterMultiplier, UseNinZaT3ProFromChart = useNinZaT3ProFromChart }, input, ref cacheMonitorIndicatorsAttached);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MonitorIndicatorsAttached MonitorIndicatorsAttached(int t3Period, int t3TCount, double t3VFactor, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, bool t3ProChaosSmoothingEnabled, int t3ProChaosSmoothingPeriod, bool t3ProFilterEnabled, double t3ProFilterMultiplier, bool useNinZaT3ProFromChart)
		{
			return indicator.MonitorIndicatorsAttached(Input, t3Period, t3TCount, t3VFactor, t3ProPeriod, t3ProTCount, t3ProVFactor, t3ProChaosSmoothingEnabled, t3ProChaosSmoothingPeriod, t3ProFilterEnabled, t3ProFilterMultiplier, useNinZaT3ProFromChart);
		}

		public Indicators.MonitorIndicatorsAttached MonitorIndicatorsAttached(ISeries<double> input , int t3Period, int t3TCount, double t3VFactor, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, bool t3ProChaosSmoothingEnabled, int t3ProChaosSmoothingPeriod, bool t3ProFilterEnabled, double t3ProFilterMultiplier, bool useNinZaT3ProFromChart)
		{
			return indicator.MonitorIndicatorsAttached(input, t3Period, t3TCount, t3VFactor, t3ProPeriod, t3ProTCount, t3ProVFactor, t3ProChaosSmoothingEnabled, t3ProChaosSmoothingPeriod, t3ProFilterEnabled, t3ProFilterMultiplier, useNinZaT3ProFromChart);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MonitorIndicatorsAttached MonitorIndicatorsAttached(int t3Period, int t3TCount, double t3VFactor, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, bool t3ProChaosSmoothingEnabled, int t3ProChaosSmoothingPeriod, bool t3ProFilterEnabled, double t3ProFilterMultiplier, bool useNinZaT3ProFromChart)
		{
			return indicator.MonitorIndicatorsAttached(Input, t3Period, t3TCount, t3VFactor, t3ProPeriod, t3ProTCount, t3ProVFactor, t3ProChaosSmoothingEnabled, t3ProChaosSmoothingPeriod, t3ProFilterEnabled, t3ProFilterMultiplier, useNinZaT3ProFromChart);
		}

		public Indicators.MonitorIndicatorsAttached MonitorIndicatorsAttached(ISeries<double> input , int t3Period, int t3TCount, double t3VFactor, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, bool t3ProChaosSmoothingEnabled, int t3ProChaosSmoothingPeriod, bool t3ProFilterEnabled, double t3ProFilterMultiplier, bool useNinZaT3ProFromChart)
		{
			return indicator.MonitorIndicatorsAttached(input, t3Period, t3TCount, t3VFactor, t3ProPeriod, t3ProTCount, t3ProVFactor, t3ProChaosSmoothingEnabled, t3ProChaosSmoothingPeriod, t3ProFilterEnabled, t3ProFilterMultiplier, useNinZaT3ProFromChart);
		}
	}
}

#endregion
