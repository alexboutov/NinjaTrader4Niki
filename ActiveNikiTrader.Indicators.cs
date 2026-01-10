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
    public partial class ActiveNikiTrader
    {
        #region Indicator Loading and Accessors
        
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
                    
                    // Native AIQ_1 indicator (from AIQ folder) - uses trendState Int32 field
                    case "AIQ_1":
                        nativeAiq1 = ind;
                        nativeAiq1TrendState = t.GetField("trendState", flagsPrivate);
                        break;
                    
                    // Native AIQ_SuperBands indicator (from AIQ folder)
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
            useChartAAA = chartAAATrendSyncEquiv != null && aaaEquivIsUptrend != null;
            
            // Determine source priority for AIQ_SuperBands: native > chart-attached equivalent > N/A
            useNativeAiqSB = nativeAiqSuperBands != null && nativeAiqSBIsUptrend != null;
            useChartSB = !useNativeAiqSB && chartAiqSuperBandsEquiv != null && sbEquivIsUptrend != null;
            
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

        // Helper methods for chart-attached indicator reading (via PropertyInfo)
        private bool GetChartBool(object o, PropertyInfo p) { try { return o != null && p != null && (bool)p.GetValue(o); } catch { return false; } }
        private double GetChartDbl(object o, PropertyInfo p) { try { return o != null && p != null ? (double)p.GetValue(o) : 0; } catch { return 0; } }
        private int GetChartInt(object o, PropertyInfo p) { try { return o != null && p != null ? (int)p.GetValue(o) : 0; } catch { return 0; } }

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
        
        // AAATrendSync - Priority: ninZa > chart-attached equivalent > N/A (no hosted equivalent)
        [Browsable(false)] public bool AAA_IsUp => aaaTrendSync != null ? GetBool(aaaTrendSync, aaaIsUptrend) : (useChartAAA ? GetChartBool(chartAAATrendSyncEquiv, aaaEquivIsUptrend) : false);
        [Browsable(false)] public bool AAA_Available => aaaTrendSync != null || useChartAAA;
        
        // AIQ_SuperBands - Priority: native > chart-attached equivalent > N/A (no hosted equivalent)
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
        
        // AIQ_1 trigger indicator - Priority: native > chart-attached equivalent > hosted equivalent
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
    }
}
