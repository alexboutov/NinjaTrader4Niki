#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
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
    /// ActiveNinZaLooker - Signal monitor for the 8-indicator ActiveNiki confluence system.
    /// 
    /// Mirrors ActiveNikiTrader signal detection as a pure Indicator (never places orders).
    /// Stays active on the chart even when discretionary trades are placed manually
    /// (NT8 disables strategies in that case, indicators are unaffected).
    ///
    /// Panel shows two states:
    ///   WATCHING  — shows live confluence count while waiting for AIQ1 flip + confirmation
    ///   SUBMIT    — shows price and direction when a signal qualifies (order SHOULD be placed manually)
    ///
    /// Confluence indicators (up to 6 selectable):
    ///   RubyRiver, DragonTrend, VIDYAPro, EasyTrend, SolarWave, T3Pro
    /// Trigger: AIQ_1 Yellow square (LONG) / Orange square (SHORT)
    /// </summary>
    public class ActiveNinZaLooker : Indicator
    {
        #region Fields

        // ── ninZa licensed indicator references ──────────────────────────────
        private object rubyRiver, vidyaPro, easyTrend, dragonTrend, solarWave, ninZaT3Pro;
        private FieldInfo rrIsUptrend, vyIsUptrend, etIsUptrend, dtPrevSignal,
                          swIsUptrend, swCountWave, t3pIsUptrend;

        // ── Native AIQ_1 ─────────────────────────────────────────────────────
        private object nativeAiq1;
        private FieldInfo nativeAiq1TrendState;
        private bool useNativeAiq1;

        // ── Chart-attached equivalent indicator references ────────────────────
        private object chartAiq1Equivalent;
        private object chartRubyRiverEquiv, chartDragonTrendEquiv, chartVidyaProEquiv;
        private object chartEasyTrendEquiv, chartSolarWaveEquiv, chartT3ProEquiv;

        private PropertyInfo aiq1IsUptrend;
        private PropertyInfo rrEquivIsUptrend, dtEquivPrevSignal, vyEquivIsUptrend;
        private PropertyInfo etEquivIsUptrend, swEquivIsUptrend, swEquivCountWave, t3pEquivIsUptrend;

        private bool useChartAiq1;
        private bool useChartRR, useChartDT, useChartVY, useChartET, useChartSW, useChartT3P;

        // ── Hosted (fallback) equivalent indicators ──────────────────────────
        private T3ProEquivalent      t3ProEquivalent;
        private VIDYAProEquivalent   vidyaProEquivalent;
        private EasyTrendEquivalent  easyTrendEquivalent;
        private RubyRiverEquivalent  rubyRiverEquivalent;
        private DragonTrendEquivalent dragonTrendEquivalent;
        private SolarWaveEquivalent  solarWaveEquivalent;
        private AIQ_1Equivalent      aiq1Equivalent;

        private bool useHostedRR, useHostedDT, useHostedVY, useHostedET, useHostedSW, useHostedT3P;
        private bool indicatorsReady;

        // ── Trigger / window tracking ─────────────────────────────────────────
        private int  barsSinceYellowSquare = -1;
        private int  barsSinceOrangeSquare = -1;
        private int  barsSinceLastSignal   = -1;
        private DateTime lastSignalTime    = DateTime.MinValue;
        private bool prevAIQ1_IsUp;
        private bool isFirstBar = true;

        // ── Previous-bar state for flip detection ─────────────────────────────
        private bool prevRR_IsUp, prevDT_IsUp, prevVY_IsUp, prevET_IsUp, prevSW_IsUp, prevT3P_IsUp;

        // ── Panel state ────────────────────────────────────────────────────────
        // Two explicit display modes
        private enum PanelMode { Watching, Submit }
        private PanelMode currentMode = PanelMode.Watching;

        // Last qualifying signal info (shown in Submit mode)
        private string submitDirection = "";     // "LONG" or "SHORT"
        private double submitPrice     = 0;
        private DateTime submitTime    = DateTime.MinValue;
        private int submitBull, submitBear, submitTotal;

        // ── Panel UI ──────────────────────────────────────────────────────────
        private Grid   controlPanel;
        private bool   panelActive;
        private bool   isDragging;
        private bool   isResizing;
        private Point  dragStartPoint;
        private double resizeStartWidth, resizeStartHeight;
        private Point  resizeStartMousePos;
        private double resizeStartLeft, resizeStartTop;
        private TranslateTransform panelTransform;
        private string panelSettingsFile;

        private enum ResizeEdge { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }
        private ResizeEdge currentResizeEdge = ResizeEdge.None;
        private const double EdgeThreshold = 8;
        private double panelWidth    = 210;
        private double panelHeight   = 340;
        private double minPanelWidth  = 160;
        private double minPanelHeight = 220;

        // Panel labels
        private TextBlock lblMode;          // "WATCHING" or "SUBMIT"
        private TextBlock lblDirectionPrice; // "LONG @ 21345.50" etc
        private TextBlock lblWindow;        // window / cooldown status
        private TextBlock lblConfluence;    // "Bull 4 / Bear 2 / 6"
        private TextBlock lblRR, lblDT, lblVY, lblET, lblSW, lblT3P;
        private TextBlock lblAIQ1;
        private TextBlock lblSignalCount;
        private Border    panelModeBorder;

        // ── Session stats ─────────────────────────────────────────────────────
        private int    signalCount;
        private string chartSessionId;

        // ── Logging ───────────────────────────────────────────────────────────
        private string       logFilePath;
        private StreamWriter logWriter;

        #endregion

        #region Parameters

        [NinjaScriptProperty]
        [Range(1, 6)]
        [Display(Name = "Min Confluence To Trade",
                 Description = "Number of the 6 confluence indicators that must agree (1-6)",
                 Order = 1, GroupName = "1. Signal Filters")]
        public int MinConfluenceToTrade { get; set; }

        [NinjaScriptProperty]
        [Range(0, 3)]
        [Display(Name = "Max Bars After Yellow/Orange Square",
                 Description = "Confirmation window after AIQ1 flip (0-3 bars)",
                 Order = 2, GroupName = "1. Signal Filters")]
        public int MaxBarsAfterSquare { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Min Solar Wave Count",
                 Order = 3, GroupName = "1. Signal Filters")]
        public int MinSolarWaveCount { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Cooldown Bars",
                 Description = "Minimum bars between signals (0 = disabled)",
                 Order = 4, GroupName = "1. Signal Filters")]
        public int CooldownBars { get; set; }

        // ── Indicator toggles ─────────────────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "Use Ruby River",   Order = 1, GroupName = "2. Confluence Indicators")]
        public bool UseRubyRiver { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Dragon Trend", Order = 2, GroupName = "2. Confluence Indicators")]
        public bool UseDragonTrend { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use VIDYA Pro",    Order = 3, GroupName = "2. Confluence Indicators")]
        public bool UseVIDYAPro { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Easy Trend",   Order = 4, GroupName = "2. Confluence Indicators")]
        public bool UseEasyTrend { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Solar Wave",   Order = 5, GroupName = "2. Confluence Indicators")]
        public bool UseSolarWave { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use T3 Pro",       Order = 6, GroupName = "2. Confluence Indicators")]
        public bool UseT3Pro { get; set; }

        // ── Hosted-fallback indicator settings ────────────────────────────────
        [NinjaScriptProperty][Range(1,100)][Display(Name="T3Pro Period",           Order=1, GroupName="3. T3 Pro Settings")]
        public int T3ProPeriod { get; set; }
        [NinjaScriptProperty][Range(1,10)] [Display(Name="T3Pro TCount",           Order=2, GroupName="3. T3 Pro Settings")]
        public int T3ProTCount { get; set; }
        [NinjaScriptProperty][Range(0.0,2.0)][Display(Name="T3Pro VFactor",        Order=3, GroupName="3. T3 Pro Settings")]
        public double T3ProVFactor { get; set; }

        [NinjaScriptProperty][Range(1,100)][Display(Name="VIDYA Period",           Order=1, GroupName="4. VIDYA Pro Settings")]
        public int VIDYAPeriod { get; set; }
        [NinjaScriptProperty][Range(1,100)][Display(Name="VIDYA Volatility Period",Order=2, GroupName="4. VIDYA Pro Settings")]
        public int VIDYAVolatilityPeriod { get; set; }

        [NinjaScriptProperty][Range(1,100)][Display(Name="EasyTrend Period",       Order=1, GroupName="5. Easy Trend Settings")]
        public int EasyTrendPeriod { get; set; }
        [NinjaScriptProperty][Range(1,200)][Display(Name="EasyTrend ATR Period",   Order=2, GroupName="5. Easy Trend Settings")]
        public int EasyTrendATRPeriod { get; set; }

        [NinjaScriptProperty][Range(1,100)][Display(Name="RubyRiver MA Period",    Order=1, GroupName="6. Ruby River Settings")]
        public int RubyRiverMAPeriod { get; set; }
        [NinjaScriptProperty][Range(0.01,2.0)][Display(Name="RubyRiver Offset Multiplier", Order=2, GroupName="6. Ruby River Settings")]
        public double RubyRiverOffsetMultiplier { get; set; }
        [NinjaScriptProperty][Range(1,200)][Display(Name="RubyRiver Offset Period",Order=3, GroupName="6. Ruby River Settings")]
        public int RubyRiverOffsetPeriod { get; set; }

        [NinjaScriptProperty][Range(1,100)][Display(Name="DragonTrend Period",     Order=1, GroupName="7. Dragon Trend Settings")]
        public int DragonTrendPeriod { get; set; }

        [NinjaScriptProperty][Range(1,200)][Display(Name="SolarWave ATR Period",   Order=1, GroupName="8. Solar Wave Settings")]
        public int SolarWaveATRPeriod { get; set; }
        [NinjaScriptProperty][Range(0.1,10.0)][Display(Name="SolarWave Trend Multiplier", Order=2, GroupName="8. Solar Wave Settings")]
        public double SolarWaveTrendMultiplier { get; set; }
        [NinjaScriptProperty][Range(0.1,10.0)][Display(Name="SolarWave Stop Multiplier",  Order=3, GroupName="8. Solar Wave Settings")]
        public double SolarWaveStopMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Sound Alert", Order = 1, GroupName = "9. Alerts")]
        public bool EnableSoundAlert { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Log Bar Details",
                 Description = "Log every bar's indicator state to file",
                 Order = 1, GroupName = "10. Debug")]
        public bool LogBarDetails { get; set; }

        #endregion

        #region OnStateChange

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                    = "ActiveNinZaLooker";
                Description             = "Watches for ActiveNiki signals (monitor-only, no orders). " +
                                          "Panel shows WATCHING or SUBMIT state with confluence count / price.";
                Calculate               = Calculate.OnBarClose;
                IsOverlay               = true;
                DisplayInDataBox        = false;
                DrawOnPricePanel        = true;
                IsSuspendedWhileInactive = false;

                // Signal filters
                MinConfluenceToTrade    = 4;
                MaxBarsAfterSquare      = 3;
                MinSolarWaveCount       = 1;
                CooldownBars            = 10;

                // Confluence indicators — all 6 on by default
                UseRubyRiver            = true;
                UseDragonTrend          = true;
                UseVIDYAPro             = true;
                UseEasyTrend            = true;
                UseSolarWave            = true;
                UseT3Pro                = true;

                // T3 Pro defaults
                T3ProPeriod             = 14;
                T3ProTCount             = 3;
                T3ProVFactor            = 0.7;

                // VIDYA Pro defaults
                VIDYAPeriod             = 9;
                VIDYAVolatilityPeriod   = 9;

                // Easy Trend defaults
                EasyTrendPeriod         = 30;
                EasyTrendATRPeriod      = 100;

                // Ruby River defaults
                RubyRiverMAPeriod       = 20;
                RubyRiverOffsetMultiplier = 0.15;
                RubyRiverOffsetPeriod   = 100;

                // Dragon Trend defaults
                DragonTrendPeriod       = 10;

                // Solar Wave defaults
                SolarWaveATRPeriod      = 100;
                SolarWaveTrendMultiplier = 2.0;
                SolarWaveStopMultiplier  = 4.0;

                EnableSoundAlert        = true;
                LogBarDetails           = false;
            }
            else if (State == State.DataLoaded)
            {
                chartSessionId = DateTime.Now.ToString("HHmmss") + "_" + new Random().Next(1000, 9999);
                InitializeLogFile();

                // Initialise hosted (fallback) equivalent indicators
                t3ProEquivalent = T3ProEquivalent(
                    T3ProMAType.EMA, T3ProPeriod, T3ProTCount, T3ProVFactor,
                    false, T3ProMAType.DEMA, 5,
                    false, 4.0, 14, true, false, "▲", "▼", 10);

                vidyaProEquivalent = VIDYAProEquivalent(
                    VIDYAPeriod, VIDYAVolatilityPeriod, true,
                    VIDYAProMAType.EMA, 5, false, 4.0, 14,
                    true, false, "▲", "▼", 10);

                easyTrendEquivalent = EasyTrendEquivalent(
                    EasyTrendMAType.EMA, EasyTrendPeriod, true,
                    EasyTrendMAType.EMA, 7, false, true,
                    0.5, EasyTrendFilterUnit.ninZaATR, EasyTrendATRPeriod,
                    true, false, "▲ + Easy", "Easy + ▼", 10);

                rubyRiverEquivalent = RubyRiverEquivalent(
                    RubyRiverMAType.EMA, RubyRiverMAPeriod, true,
                    RubyRiverMAType.LinReg, 5, RubyRiverOffsetMultiplier, RubyRiverOffsetPeriod,
                    true, false, "▲", "▼", 10);

                dragonTrendEquivalent = DragonTrendEquivalent(
                    DragonTrendPeriod, true,
                    DragonTrendMAType.EMA, 5, false, "▲", "▼", 10);

                solarWaveEquivalent = SolarWaveEquivalent(
                    SolarWaveATRPeriod, SolarWaveTrendMultiplier, SolarWaveStopMultiplier,
                    2, 1, 5, 10, 10, true, false, "▲ + Trend", "Trend + ▼", 12);

                aiq1Equivalent = AIQ_1Equivalent(
                    3, 0, AIQ1EquivMAMethod.MA1, true, 0.05, 0.05, 0.03, 0.03,
                    true, 15, 100, false, 4, Brushes.Orange, Brushes.Orange);

                Log($"ActiveNinZaLooker started | MinConf={MinConfluenceToTrade} | MaxBars={MaxBarsAfterSquare} | Cooldown={CooldownBars}");
            }
            else if (State == State.Historical)
            {
                LoadIndicators();
                LogDetectedIndicators();
                if (ChartControl != null)
                    ChartControl.Dispatcher.InvokeAsync(CreatePanel);
            }
            else if (State == State.Terminated)
            {
                if (ChartControl != null)
                    ChartControl.Dispatcher.InvokeAsync(RemovePanel);
                CloseLogFile();
            }
        }

        #endregion

        #region Indicator Loading

        private void LoadIndicators()
        {
            if (ChartControl?.Indicators == null)
            {
                useHostedRR = useHostedDT = useHostedVY = useHostedET = useHostedSW = useHostedT3P = true;
                useNativeAiq1 = useChartAiq1 = false;
                indicatorsReady = true;
                return;
            }

            var priv = BindingFlags.NonPublic | BindingFlags.Instance;
            var pub  = BindingFlags.Public    | BindingFlags.Instance;

            foreach (var ind in ChartControl.Indicators)
            {
                var t    = ind.GetType();
                var name = t.Name;

                switch (name)
                {
                    // ninZa licensed
                    case "ninZaRubyRiver":
                        rubyRiver    = ind;
                        rrIsUptrend  = t.GetField("isUptrend", priv);
                        break;
                    case "ninZaVIDYAPro":
                        vidyaPro     = ind;
                        vyIsUptrend  = t.GetField("isUptrend", priv);
                        break;
                    case "ninZaEasyTrend":
                        easyTrend    = ind;
                        etIsUptrend  = t.GetField("isUptrend", priv);
                        break;
                    case "ninZaDragonTrend":
                        dragonTrend  = ind;
                        dtPrevSignal = t.GetField("prevSignal", priv);
                        break;
                    case "ninZaSolarWave":
                        solarWave    = ind;
                        swIsUptrend  = t.GetField("isUptrend", priv);
                        swCountWave  = t.GetField("countWave", priv);
                        break;
                    case "ninZaT3Pro":
                        ninZaT3Pro   = ind;
                        t3pIsUptrend = t.GetField("isUptrend", priv);
                        break;

                    // Native AIQ_1
                    case "AIQ_1":
                        nativeAiq1          = ind;
                        nativeAiq1TrendState = t.GetField("trendState", priv);
                        break;

                    // Chart-attached equivalents
                    case "AIQ_1Equivalent":
                        chartAiq1Equivalent = ind;
                        aiq1IsUptrend       = t.GetProperty("IsUptrend", pub);
                        break;
                    case "RubyRiverEquivalent":
                        chartRubyRiverEquiv = ind;
                        rrEquivIsUptrend    = t.GetProperty("IsUptrend", pub);
                        break;
                    case "DragonTrendEquivalent":
                        chartDragonTrendEquiv = ind;
                        dtEquivPrevSignal     = t.GetProperty("PrevSignal", pub);
                        break;
                    case "VIDYAProEquivalent":
                        chartVidyaProEquiv  = ind;
                        vyEquivIsUptrend    = t.GetProperty("IsUptrend", pub);
                        break;
                    case "EasyTrendEquivalent":
                        chartEasyTrendEquiv = ind;
                        etEquivIsUptrend    = t.GetProperty("IsUptrend", pub);
                        break;
                    case "SolarWaveEquivalent":
                        chartSolarWaveEquiv = ind;
                        swEquivIsUptrend    = t.GetProperty("IsUptrend", pub);
                        swEquivCountWave    = t.GetProperty("CountWave",  pub);
                        break;
                    case "T3ProEquivalent":
                        chartT3ProEquiv     = ind;
                        t3pEquivIsUptrend   = t.GetProperty("IsUptrend", pub);
                        break;
                }
            }

            // Resolve priority: ninZa/native > chart-attached > hosted fallback
            useNativeAiq1 = nativeAiq1 != null && nativeAiq1TrendState != null;
            useChartAiq1  = !useNativeAiq1 && chartAiq1Equivalent != null && aiq1IsUptrend != null;

            useChartRR  = chartRubyRiverEquiv  != null && rrEquivIsUptrend  != null;
            useChartDT  = chartDragonTrendEquiv != null && dtEquivPrevSignal != null;
            useChartVY  = chartVidyaProEquiv    != null && vyEquivIsUptrend  != null;
            useChartET  = chartEasyTrendEquiv   != null && etEquivIsUptrend  != null;
            useChartSW  = chartSolarWaveEquiv   != null && swEquivIsUptrend  != null;
            useChartT3P = chartT3ProEquiv       != null && t3pEquivIsUptrend != null;

            useHostedRR  = rubyRiver    == null && !useChartRR;
            useHostedDT  = dragonTrend  == null && !useChartDT;
            useHostedVY  = vidyaPro     == null && !useChartVY;
            useHostedET  = easyTrend    == null && !useChartET;
            useHostedSW  = solarWave    == null && !useChartSW;
            useHostedT3P = ninZaT3Pro   == null && !useChartT3P;

            indicatorsReady = true;
        }

        private void LogDetectedIndicators()
        {
            Log("--- Detected Indicators ---");
            Log($"  AIQ_1     : {(useNativeAiq1 ? "NATIVE" : useChartAiq1 ? "CHART" : "HOSTED")}");
            Log($"  RubyRiver : {SrcLabel(rubyRiver != null, useChartRR)}");
            Log($"  DragonTrend: {SrcLabel(dragonTrend != null, useChartDT)}");
            Log($"  VIDYAPro  : {SrcLabel(vidyaPro != null, useChartVY)}");
            Log($"  EasyTrend : {SrcLabel(easyTrend != null, useChartET)}");
            Log($"  SolarWave : {SrcLabel(solarWave != null, useChartSW)}");
            Log($"  T3Pro     : {SrcLabel(ninZaT3Pro != null, useChartT3P)}");
            Log("---------------------------");
        }

        private string SrcLabel(bool hasNinZa, bool hasChart)
            => hasNinZa ? "ninZa" : hasChart ? "CHART" : "HOSTED";

        #endregion

        #region Indicator Accessors

        private bool   GF(object o, FieldInfo  f) { try { return o != null && f != null && (bool)f.GetValue(o); } catch { return false; } }
        private double GD(object o, FieldInfo  f) { try { return o != null && f != null ? (double)f.GetValue(o) : 0; } catch { return 0; } }
        private int    GI(object o, FieldInfo  f) { try { return o != null && f != null ? (int)f.GetValue(o)   : 0; } catch { return 0; } }
        private bool   GP(object o, PropertyInfo p) { try { return o != null && p != null && (bool)p.GetValue(o); } catch { return false; } }
        private double GD(object o, PropertyInfo p) { try { return o != null && p != null ? (double)p.GetValue(o) : 0; } catch { return 0; } }
        private int    GI(object o, PropertyInfo p) { try { return o != null && p != null ? (int)p.GetValue(o)   : 0; } catch { return 0; } }

        [Browsable(false)]
        public bool AIQ1_IsUp
        {
            get
            {
                if (useNativeAiq1) return GI(nativeAiq1, nativeAiq1TrendState) > 0;
                if (useChartAiq1)  return GP(chartAiq1Equivalent, aiq1IsUptrend);
                return aiq1Equivalent?.IsUptrend ?? false;
            }
        }

        [Browsable(false)]
        public bool RR_IsUp =>
            rubyRiver != null ? GF(rubyRiver, rrIsUptrend)
            : useChartRR      ? GP(chartRubyRiverEquiv, rrEquivIsUptrend)
                               : (rubyRiverEquivalent?.IsUptrend ?? false);

        [Browsable(false)]
        public double DT_Signal =>
            dragonTrend != null ? GD(dragonTrend, dtPrevSignal)
            : useChartDT        ? GD(chartDragonTrendEquiv, dtEquivPrevSignal)
                                 : (dragonTrendEquivalent?.PrevSignal ?? 0);

        [Browsable(false)] public bool DT_IsUp   => DT_Signal > 0;
        [Browsable(false)] public bool DT_IsDown => DT_Signal < 0;

        [Browsable(false)]
        public bool VY_IsUp =>
            vidyaPro != null ? GF(vidyaPro, vyIsUptrend)
            : useChartVY     ? GP(chartVidyaProEquiv, vyEquivIsUptrend)
                              : (vidyaProEquivalent?.IsUptrend ?? false);

        [Browsable(false)]
        public bool ET_IsUp =>
            easyTrend != null ? GF(easyTrend, etIsUptrend)
            : useChartET      ? GP(chartEasyTrendEquiv, etEquivIsUptrend)
                               : (easyTrendEquivalent?.IsUptrend ?? false);

        [Browsable(false)]
        public bool SW_IsUp =>
            solarWave != null ? GF(solarWave, swIsUptrend)
            : useChartSW      ? GP(chartSolarWaveEquiv, swEquivIsUptrend)
                               : (solarWaveEquivalent?.IsUptrend ?? false);

        [Browsable(false)]
        public int SW_Count =>
            solarWave != null ? GI(solarWave, swCountWave)
            : useChartSW      ? GI(chartSolarWaveEquiv, swEquivCountWave)
                               : (solarWaveEquivalent?.CountWave ?? 0);

        [Browsable(false)]
        public bool T3P_IsUp =>
            ninZaT3Pro != null ? GF(ninZaT3Pro, t3pIsUptrend)
            : useChartT3P      ? GP(chartT3ProEquiv, t3pEquivIsUptrend)
                                : (t3ProEquivalent?.IsUptrend ?? false);

        #endregion

        #region Confluence

        private (int bull, int bear, int total) GetConfluence()
        {
            int bull = 0, bear = 0, total = 0;

            if (UseRubyRiver)   { total++; if (RR_IsUp)                                   bull++; else bear++; }
            if (UseDragonTrend) { total++; if (DT_IsUp)                                   bull++; else if (DT_IsDown) bear++; }
            if (UseVIDYAPro)    { total++; if (VY_IsUp)                                   bull++; else bear++; }
            if (UseEasyTrend)   { total++; if (ET_IsUp)                                   bull++; else bear++; }
            if (UseSolarWave)   { total++; if (SW_IsUp && SW_Count >= MinSolarWaveCount)  bull++; else if (!SW_IsUp && SW_Count <= -MinSolarWaveCount) bear++; }
            if (UseT3Pro)       { total++; if (T3P_IsUp)                                  bull++; else bear++; }

            return (bull, bear, total);
        }

        // Returns the name of the first indicator that confirms the direction.
        // Prefers a fresh flip (this bar) over already-in-state.
        private string FindBullishConfirmation()
        {
            // fresh flips first
            if (UseRubyRiver   && RR_IsUp  && !prevRR_IsUp)  return "RR";
            if (UseDragonTrend && DT_IsUp  && !prevDT_IsUp)  return "DT";
            if (UseVIDYAPro    && VY_IsUp  && !prevVY_IsUp)  return "VY";
            if (UseEasyTrend   && ET_IsUp  && !prevET_IsUp)  return "ET";
            if (UseSolarWave   && SW_IsUp  && !prevSW_IsUp)  return "SW";
            if (UseT3Pro       && T3P_IsUp && !prevT3P_IsUp) return "T3P";
            // already aligned
            if (UseRubyRiver   && RR_IsUp)  return "RR";
            if (UseDragonTrend && DT_IsUp)  return "DT";
            if (UseVIDYAPro    && VY_IsUp)  return "VY";
            if (UseEasyTrend   && ET_IsUp)  return "ET";
            if (UseSolarWave   && SW_IsUp)  return "SW";
            if (UseT3Pro       && T3P_IsUp) return "T3P";
            return null;
        }

        private string FindBearishConfirmation()
        {
            if (UseRubyRiver   && !RR_IsUp   && prevRR_IsUp)  return "RR";
            if (UseDragonTrend && DT_IsDown  && prevDT_IsUp)  return "DT";
            if (UseVIDYAPro    && !VY_IsUp   && prevVY_IsUp)  return "VY";
            if (UseEasyTrend   && !ET_IsUp   && prevET_IsUp)  return "ET";
            if (UseSolarWave   && !SW_IsUp   && prevSW_IsUp)  return "SW";
            if (UseT3Pro       && !T3P_IsUp  && prevT3P_IsUp) return "T3P";
            if (UseRubyRiver   && !RR_IsUp)  return "RR";
            if (UseDragonTrend && DT_IsDown) return "DT";
            if (UseVIDYAPro    && !VY_IsUp)  return "VY";
            if (UseEasyTrend   && !ET_IsUp)  return "ET";
            if (UseSolarWave   && !SW_IsUp)  return "SW";
            if (UseT3Pro       && !T3P_IsUp) return "T3P";
            return null;
        }

        #endregion

        #region OnBarUpdate

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 20 || !indicatorsReady) return;

            DateTime barTime = Time[0];

            // ── Bar detail logging ──────────────────────────────────────────
            if (LogBarDetails)
            {
                var (b, br, tot) = GetConfluence();
                Log($"[BAR {CurrentBar}] {barTime:HH:mm:ss} | C={Close[0]:F2} " +
                    $"AIQ1={U(AIQ1_IsUp)} RR={U(RR_IsUp)} DT={DT_Signal:0} VY={U(VY_IsUp)} " +
                    $"ET={U(ET_IsUp)} SW={SW_Count} T3P={U(T3P_IsUp)} Bull={b} Bear={br}");
            }

            // ── Cooldown ────────────────────────────────────────────────────
            if (barsSinceLastSignal >= 0) barsSinceLastSignal++;
            bool inCooldown = CooldownBars > 0
                           && barsSinceLastSignal >= 0
                           && barsSinceLastSignal < CooldownBars;

            // ── AIQ1 flip detection ─────────────────────────────────────────
            bool yellowAppeared = AIQ1_IsUp && !prevAIQ1_IsUp && !isFirstBar;
            bool orangeAppeared = !AIQ1_IsUp && prevAIQ1_IsUp && !isFirstBar;

            if (yellowAppeared)
            {
                barsSinceYellowSquare = 0;
                barsSinceOrangeSquare = -1;
                Log(inCooldown
                    ? $"🟨 Yellow ■ @ {barTime:HH:mm:ss} | BLOCKED by cooldown ({barsSinceLastSignal}/{CooldownBars})"
                    : $"🟨 Yellow ■ @ {barTime:HH:mm:ss} | LONG window OPEN");
            }
            else if (orangeAppeared)
            {
                barsSinceOrangeSquare = 0;
                barsSinceYellowSquare = -1;
                Log(inCooldown
                    ? $"🟧 Orange ■ @ {barTime:HH:mm:ss} | BLOCKED by cooldown ({barsSinceLastSignal}/{CooldownBars})"
                    : $"🟧 Orange ■ @ {barTime:HH:mm:ss} | SHORT window OPEN");
            }
            else
            {
                if (barsSinceYellowSquare >= 0)
                {
                    barsSinceYellowSquare++;
                    if (barsSinceYellowSquare > MaxBarsAfterSquare)
                    {
                        Log($"LONG window EXPIRED @ {barTime:HH:mm:ss} (>{MaxBarsAfterSquare} bars)");
                        barsSinceYellowSquare = -1;
                    }
                }
                if (barsSinceOrangeSquare >= 0)
                {
                    barsSinceOrangeSquare++;
                    if (barsSinceOrangeSquare > MaxBarsAfterSquare)
                    {
                        Log($"SHORT window EXPIRED @ {barTime:HH:mm:ss} (>{MaxBarsAfterSquare} bars)");
                        barsSinceOrangeSquare = -1;
                    }
                }
            }

            // ── Always refresh panel (window/confluence display) ────────────
            RefreshPanel();

            if (inCooldown)
            {
                SavePrevState();
                return;
            }

            // ── LONG signal check ───────────────────────────────────────────
            if (barsSinceYellowSquare >= 0 && barsSinceYellowSquare <= MaxBarsAfterSquare)
            {
                string conf = FindBullishConfirmation();
                if (conf != null)
                {
                    var (bull, bear, total) = GetConfluence();
                    if (bull >= MinConfluenceToTrade)
                    {
                        FireSignal("LONG", conf, barTime, bull, bear, total);
                        barsSinceYellowSquare = -1;
                        barsSinceLastSignal   = 0;
                        lastSignalTime        = barTime;
                    }
                    else
                    {
                        Log($"{conf} seen but bull {bull}/{total} < {MinConfluenceToTrade} @ {barTime:HH:mm:ss}");
                    }
                }
            }

            // ── SHORT signal check ──────────────────────────────────────────
            if (barsSinceOrangeSquare >= 0 && barsSinceOrangeSquare <= MaxBarsAfterSquare)
            {
                string conf = FindBearishConfirmation();
                if (conf != null)
                {
                    var (bull, bear, total) = GetConfluence();
                    if (bear >= MinConfluenceToTrade)
                    {
                        FireSignal("SHORT", conf, barTime, bull, bear, total);
                        barsSinceOrangeSquare = -1;
                        barsSinceLastSignal   = 0;
                        lastSignalTime        = barTime;
                    }
                    else
                    {
                        Log($"{conf} seen but bear {bear}/{total} < {MinConfluenceToTrade} @ {barTime:HH:mm:ss}");
                    }
                }
            }

            SavePrevState();
        }

        private void FireSignal(string dir, string confirming, DateTime t,
                                int bull, int bear, int total)
        {
            signalCount++;
            submitDirection = dir;
            submitPrice     = Close[0];
            submitTime      = t;
            submitBull      = bull;
            submitBear      = bear;
            submitTotal     = total;
            currentMode     = PanelMode.Submit;

            int aligned = dir == "LONG" ? bull : bear;

            Log("");
            Log("╔════════════════════════════════════════╗");
            Log($"║  *** {dir} SIGNAL @ {t:HH:mm:ss} ***");
            Log("╠════════════════════════════════════════╣");
            Log($"║  Price      : {Close[0]:F2}");
            Log($"║  Confirming : {confirming}");
            Log($"║  Confluence : {aligned}/{total}");
            Log($"║  RR={U(RR_IsUp)} DT={DT_Signal:0} VY={U(VY_IsUp)} ET={U(ET_IsUp)} SW={SW_Count} T3P={U(T3P_IsUp)}");
            Log($"║  AIQ1={U(AIQ1_IsUp)}");
            Log("╚════════════════════════════════════════╝");

            if (EnableSoundAlert)
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }

            RefreshPanel();
        }

        private void SavePrevState()
        {
            prevAIQ1_IsUp = AIQ1_IsUp;
            prevRR_IsUp   = RR_IsUp;
            prevDT_IsUp   = DT_IsUp;
            prevVY_IsUp   = VY_IsUp;
            prevET_IsUp   = ET_IsUp;
            prevSW_IsUp   = SW_IsUp;
            prevT3P_IsUp  = T3P_IsUp;
            isFirstBar    = false;
        }

        private string U(bool up) => up ? "UP" : "DN";

        #endregion

        #region Panel UI

        private void CreatePanel()
        {
            try
            {
                string settingsDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NinjaTrader 8", "settings");
                panelSettingsFile = System.IO.Path.Combine(settingsDir, "ActiveNinZaLooker_Panel.txt");
                panelTransform    = new TranslateTransform(20, 20);

                LoadPanelSettings();

                controlPanel = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment   = VerticalAlignment.Top,
                    Background          = new SolidColorBrush(Color.FromArgb(235, 20, 22, 32)),
                    Width               = panelWidth,
                    MinWidth            = minPanelWidth,
                    MinHeight           = minPanelHeight,
                    RenderTransform     = panelTransform,
                    Cursor              = Cursors.Arrow
                };

                controlPanel.MouseLeftButtonDown += Panel_MouseDown;
                controlPanel.MouseLeftButtonUp   += Panel_MouseUp;
                controlPanel.MouseMove           += Panel_MouseMove;
                controlPanel.MouseLeave          += Panel_MouseLeave;

                var outerBorder = new Border
                {
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(70, 70, 95)),
                    BorderThickness = new Thickness(2),
                    CornerRadius    = new CornerRadius(5),
                    Padding         = new Thickness(8)
                };

                var stack = new StackPanel();

                // ── Header ────────────────────────────────────────────────────
                stack.Children.Add(new TextBlock
                {
                    Text                = "ActiveNinZaLooker",
                    FontWeight          = FontWeights.Bold,
                    Foreground          = Brushes.CornflowerBlue,
                    FontSize            = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin              = new Thickness(0, 0, 0, 2)
                });
                stack.Children.Add(new TextBlock
                {
                    Text                = "Signal Monitor · No Orders",
                    Foreground          = Brushes.Gray,
                    FontSize            = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin              = new Thickness(0, 0, 0, 6)
                });

                // ── Mode border (WATCHING / SUBMIT) ────────────────────────
                panelModeBorder = new Border
                {
                    BorderThickness = new Thickness(2),
                    CornerRadius    = new CornerRadius(4),
                    Padding         = new Thickness(4),
                    Margin          = new Thickness(0, 0, 0, 6)
                };
                var modeStack = new StackPanel();

                lblMode = new TextBlock
                {
                    Text                = "● WATCHING",
                    FontWeight          = FontWeights.Bold,
                    FontSize            = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground          = Brushes.DodgerBlue
                };
                modeStack.Children.Add(lblMode);

                lblDirectionPrice = new TextBlock
                {
                    Text                = "",
                    FontSize            = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground          = Brushes.White,
                    Margin              = new Thickness(0, 2, 0, 0)
                };
                modeStack.Children.Add(lblDirectionPrice);

                panelModeBorder.Child = modeStack;
                stack.Children.Add(panelModeBorder);

                // ── Window / cooldown ─────────────────────────────────────────
                lblWindow = new TextBlock
                {
                    Text                = "Window: CLOSED",
                    Foreground          = Brushes.Gray,
                    FontSize            = 9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin              = new Thickness(0, 0, 0, 4)
                };
                stack.Children.Add(lblWindow);

                // ── Confluence summary ────────────────────────────────────────
                lblConfluence = new TextBlock
                {
                    Text                = "Bull: - / Bear: - / 6",
                    Foreground          = Brushes.LightGray,
                    FontSize            = 9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin              = new Thickness(0, 0, 0, 4)
                };
                stack.Children.Add(lblConfluence);

                // ── Divider ────────────────────────────────────────────────────
                stack.Children.Add(new Border
                {
                    BorderBrush     = Brushes.DimGray,
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Margin          = new Thickness(0, 2, 0, 4)
                });

                // ── AIQ1 trigger row ──────────────────────────────────────────
                stack.Children.Add(MakeIndRow("AIQ_1 Trigger", out lblAIQ1, Brushes.Yellow));

                // ── Confluence indicator rows ─────────────────────────────────
                stack.Children.Add(new TextBlock
                {
                    Text      = "── Confluence ──",
                    Foreground = Brushes.DimGray,
                    FontSize  = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin    = new Thickness(0, 2, 0, 2)
                });
                stack.Children.Add(MakeIndRow("Ruby River",   out lblRR,  Brushes.White));
                stack.Children.Add(MakeIndRow("Dragon Trend", out lblDT,  Brushes.White));
                stack.Children.Add(MakeIndRow("VIDYA Pro",    out lblVY,  Brushes.White));
                stack.Children.Add(MakeIndRow("Easy Trend",   out lblET,  Brushes.White));
                stack.Children.Add(MakeIndRow("Solar Wave",   out lblSW,  Brushes.White));
                stack.Children.Add(MakeIndRow("T3 Pro",       out lblT3P, Brushes.White));

                // ── Session count ─────────────────────────────────────────────
                stack.Children.Add(new Border
                {
                    BorderBrush     = Brushes.DimGray,
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Margin          = new Thickness(0, 4, 0, 4)
                });
                lblSignalCount = new TextBlock
                {
                    Text       = "Signals today: 0",
                    Foreground  = Brushes.Gray,
                    FontSize   = 8,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                stack.Children.Add(lblSignalCount);

                // ── Resize grip ───────────────────────────────────────────────
                var grip = new Canvas
                {
                    Width               = 12,
                    Height              = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin              = new Thickness(0, 4, 0, 0)
                };
                for (int i = 0; i < 3; i++)
                    grip.Children.Add(new Line
                    {
                        X1 = 10 - i * 4, Y1 = 10,
                        X2 = 10,         Y2 = 10 - i * 4,
                        Stroke          = new SolidColorBrush(Color.FromRgb(100, 100, 120)),
                        StrokeThickness = 1
                    });
                stack.Children.Add(grip);

                outerBorder.Child = stack;
                controlPanel.Children.Add(outerBorder);

                (ChartControl.Parent as Grid)?.Children.Add(controlPanel);
                panelActive = true;

                ApplyPanelConstraints();
            }
            catch (Exception ex) { Print($"[ActiveNinZaLooker] Panel error: {ex.Message}"); }
        }

        private Grid MakeIndRow(string name, out TextBlock statusLbl, Brush nameBrush)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

            var txt = new TextBlock
            {
                Text              = name,
                Foreground        = nameBrush,
                FontSize          = 9,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(txt, 0);
            row.Children.Add(txt);

            statusLbl = new TextBlock
            {
                Text                = "---",
                Foreground          = Brushes.Gray,
                FontSize            = 9,
                FontWeight          = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Center
            };
            Grid.SetColumn(statusLbl, 1);
            row.Children.Add(statusLbl);

            return row;
        }

        private void RemovePanel()
        {
            try
            {
                if (panelActive && controlPanel != null)
                {
                    controlPanel.MouseLeftButtonDown -= Panel_MouseDown;
                    controlPanel.MouseLeftButtonUp   -= Panel_MouseUp;
                    controlPanel.MouseMove           -= Panel_MouseMove;
                    controlPanel.MouseLeave          -= Panel_MouseLeave;
                    (ChartControl?.Parent as Grid)?.Children.Remove(controlPanel);
                    panelActive = false;
                }
            }
            catch { }
        }

        // ── Panel refresh (called from OnBarUpdate via Dispatcher) ────────────
        private void RefreshPanel()
        {
            if (!panelActive || ChartControl == null) return;
            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var (bull, bear, total) = GetConfluence();

                    // ── Mode block ────────────────────────────────────────────
                    if (currentMode == PanelMode.Submit)
                    {
                        bool isLong = submitDirection == "LONG";
                        lblMode.Text      = isLong ? "▲ SUBMIT LONG" : "▼ SUBMIT SHORT";
                        lblMode.Foreground = isLong ? Brushes.Lime : Brushes.OrangeRed;
                        panelModeBorder.BorderBrush = isLong
                            ? new SolidColorBrush(Color.FromRgb(0, 220, 80))
                            : new SolidColorBrush(Color.FromRgb(220, 60, 0));
                        panelModeBorder.Background = isLong
                            ? new SolidColorBrush(Color.FromArgb(40, 0, 255, 80))
                            : new SolidColorBrush(Color.FromArgb(40, 255, 80, 0));

                        int aligned = isLong ? submitBull : submitBear;
                        lblDirectionPrice.Text      = $"{submitDirection} @ {submitPrice:F2}  [{aligned}/{submitTotal}]  {submitTime:HH:mm:ss}";
                        lblDirectionPrice.Foreground = isLong ? Brushes.Lime : Brushes.OrangeRed;
                        lblDirectionPrice.Visibility = Visibility.Visible;
                    }
                    else // Watching
                    {
                        lblMode.Text      = "● WATCHING";
                        lblMode.Foreground = Brushes.DodgerBlue;
                        panelModeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(40, 80, 160));
                        panelModeBorder.Background  = new SolidColorBrush(Color.FromArgb(30, 40, 80, 200));

                        // Hint: show count toward threshold
                        bool bullReady = bull >= MinConfluenceToTrade;
                        bool bearReady = bear >= MinConfluenceToTrade;
                        if (bullReady || bearReady)
                        {
                            lblDirectionPrice.Text = bullReady
                                ? $"Bull {bull}/{MinConfluenceToTrade} — await Yellow ■"
                                : $"Bear {bear}/{MinConfluenceToTrade} — await Orange ■";
                            lblDirectionPrice.Foreground = bullReady ? Brushes.Lime : Brushes.Orange;
                        }
                        else
                        {
                            lblDirectionPrice.Text      = $"Need {MinConfluenceToTrade} | Bull {bull} Bear {bear}";
                            lblDirectionPrice.Foreground = Brushes.Gray;
                        }
                        lblDirectionPrice.Visibility = Visibility.Visible;
                    }

                    // ── Window status ─────────────────────────────────────────
                    bool inCD = CooldownBars > 0 && barsSinceLastSignal >= 0 && barsSinceLastSignal < CooldownBars;
                    if (inCD)
                    {
                        lblWindow.Text      = $"🕐 Cooldown {barsSinceLastSignal}/{CooldownBars}";
                        lblWindow.Foreground = Brushes.Yellow;
                    }
                    else if (barsSinceYellowSquare >= 0 && barsSinceYellowSquare <= MaxBarsAfterSquare)
                    {
                        lblWindow.Text      = $"⚡ LONG Window ({barsSinceYellowSquare}/{MaxBarsAfterSquare})";
                        lblWindow.Foreground = Brushes.Lime;
                    }
                    else if (barsSinceOrangeSquare >= 0 && barsSinceOrangeSquare <= MaxBarsAfterSquare)
                    {
                        lblWindow.Text      = $"⚡ SHORT Window ({barsSinceOrangeSquare}/{MaxBarsAfterSquare})";
                        lblWindow.Foreground = Brushes.Orange;
                    }
                    else
                    {
                        lblWindow.Text      = "Window: CLOSED";
                        lblWindow.Foreground = Brushes.Gray;
                    }

                    // ── Confluence line ───────────────────────────────────────
                    lblConfluence.Text = $"Bull: {bull}  Bear: {bear}  / {total}  (need {MinConfluenceToTrade})";

                    // ── AIQ1 ──────────────────────────────────────────────────
                    SetLbl(lblAIQ1, AIQ1_IsUp, true);

                    // ── Confluence indicators ─────────────────────────────────
                    SetLbl(lblRR,  RR_IsUp,  UseRubyRiver);
                    SetLbl(lblDT,  DT_IsUp,  UseDragonTrend);
                    SetLbl(lblVY,  VY_IsUp,  UseVIDYAPro);
                    SetLbl(lblET,  ET_IsUp,  UseEasyTrend);
                    SetLbl(lblSW,  SW_IsUp,  UseSolarWave);
                    SetLbl(lblT3P, T3P_IsUp, UseT3Pro);

                    // ── Signal count ──────────────────────────────────────────
                    if (lblSignalCount != null)
                        lblSignalCount.Text = $"Signals today: {signalCount}";
                }
                catch { }
            });
        }

        private void SetLbl(TextBlock lbl, bool isUp, bool enabled)
        {
            if (lbl == null) return;
            if (!enabled) { lbl.Text = "OFF"; lbl.Foreground = Brushes.DimGray; return; }
            lbl.Text      = isUp ? "UP" : "DN";
            lbl.Foreground = isUp ? Brushes.Lime : Brushes.OrangeRed;
        }

        #endregion

        #region Panel Drag / Resize

        private ResizeEdge GetEdge(Point p)
        {
            double w = controlPanel.ActualWidth  > 0 ? controlPanel.ActualWidth  : panelWidth;
            double h = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : panelHeight;
            bool L = p.X <= EdgeThreshold, R = p.X >= w - EdgeThreshold;
            bool T = p.Y <= EdgeThreshold, B = p.Y >= h - EdgeThreshold;
            if (T && L) return ResizeEdge.TopLeft;
            if (T && R) return ResizeEdge.TopRight;
            if (B && L) return ResizeEdge.BottomLeft;
            if (B && R) return ResizeEdge.BottomRight;
            if (L) return ResizeEdge.Left;
            if (R) return ResizeEdge.Right;
            if (T) return ResizeEdge.Top;
            if (B) return ResizeEdge.Bottom;
            return ResizeEdge.None;
        }

        private Cursor CursorFor(ResizeEdge e)
        {
            switch (e)
            {
                case ResizeEdge.Left: case ResizeEdge.Right:             return Cursors.SizeWE;
                case ResizeEdge.Top:  case ResizeEdge.Bottom:            return Cursors.SizeNS;
                case ResizeEdge.TopLeft: case ResizeEdge.BottomRight:    return Cursors.SizeNWSE;
                case ResizeEdge.TopRight: case ResizeEdge.BottomLeft:    return Cursors.SizeNESW;
                default: return Cursors.Hand;
            }
        }

        private void Panel_MouseDown(object s, MouseButtonEventArgs e)
        {
            var parent = ChartControl?.Parent as UIElement;
            var edge   = GetEdge(e.GetPosition(controlPanel));
            if (edge != ResizeEdge.None)
            {
                currentResizeEdge    = edge;
                isResizing           = true;
                resizeStartMousePos  = e.GetPosition(parent);
                resizeStartWidth     = controlPanel.ActualWidth  > 0 ? controlPanel.ActualWidth  : panelWidth;
                resizeStartHeight    = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : panelHeight;
                resizeStartLeft      = panelTransform.X;
                resizeStartTop       = panelTransform.Y;
            }
            else
            {
                isDragging      = true;
                dragStartPoint  = e.GetPosition(parent);
                dragStartPoint.X -= panelTransform.X;
                dragStartPoint.Y -= panelTransform.Y;
            }
            controlPanel.CaptureMouse();
            e.Handled = true;
        }

        private void Panel_MouseUp(object s, MouseButtonEventArgs e)
        {
            if (isDragging || isResizing)
            {
                isDragging = isResizing = false;
                currentResizeEdge = ResizeEdge.None;
                controlPanel.ReleaseMouseCapture();
                SavePanelSettings();
                e.Handled = true;
            }
        }

        private void Panel_MouseLeave(object s, MouseEventArgs e)
        {
            if (!isDragging && !isResizing)
                controlPanel.Cursor = Cursors.Arrow;
        }

        private void Panel_MouseMove(object s, MouseEventArgs e)
        {
            var parent = ChartControl?.Parent as FrameworkElement;
            if (parent == null) return;
            var cur = e.GetPosition(parent);

            if (isResizing)
            {
                double dx = cur.X - resizeStartMousePos.X;
                double dy = cur.Y - resizeStartMousePos.Y;
                double nW = resizeStartWidth, nH = resizeStartHeight;
                double nL = resizeStartLeft,  nT = resizeStartTop;

                switch (currentResizeEdge)
                {
                    case ResizeEdge.Right:       nW = resizeStartWidth  + dx; break;
                    case ResizeEdge.Left:        nW = resizeStartWidth  - dx; nL = resizeStartLeft + dx; break;
                    case ResizeEdge.Bottom:      nH = resizeStartHeight + dy; break;
                    case ResizeEdge.Top:         nH = resizeStartHeight - dy; nT = resizeStartTop  + dy; break;
                    case ResizeEdge.BottomRight: nW = resizeStartWidth  + dx; nH = resizeStartHeight + dy; break;
                    case ResizeEdge.BottomLeft:  nW = resizeStartWidth  - dx; nL = resizeStartLeft + dx; nH = resizeStartHeight + dy; break;
                    case ResizeEdge.TopRight:    nW = resizeStartWidth  + dx; nH = resizeStartHeight - dy; nT = resizeStartTop + dy; break;
                    case ResizeEdge.TopLeft:     nW = resizeStartWidth  - dx; nL = resizeStartLeft + dx; nH = resizeStartHeight - dy; nT = resizeStartTop + dy; break;
                }

                nW = Math.Max(minPanelWidth,  nW);
                nH = Math.Max(minPanelHeight, nH);
                nL = Math.Max(0, Math.Min(parent.ActualWidth  - nW, nL));
                nT = Math.Max(0, Math.Min(parent.ActualHeight - nH, nT));

                panelWidth  = nW; panelHeight = nH;
                controlPanel.Width  = nW;
                controlPanel.Height = nH;
                panelTransform.X = nL;
                panelTransform.Y = nT;
                e.Handled = true;
            }
            else if (isDragging)
            {
                double w = controlPanel.ActualWidth  > 0 ? controlPanel.ActualWidth  : panelWidth;
                double h = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : panelHeight;
                panelTransform.X = Math.Max(0, Math.Min(parent.ActualWidth  - w, cur.X - dragStartPoint.X));
                panelTransform.Y = Math.Max(0, Math.Min(parent.ActualHeight - h, cur.Y - dragStartPoint.Y));
                e.Handled = true;
            }
            else
            {
                controlPanel.Cursor = CursorFor(GetEdge(e.GetPosition(controlPanel)));
            }
        }

        private void ApplyPanelConstraints()
        {
            var parent = ChartControl?.Parent as FrameworkElement;
            if (parent == null || controlPanel == null) return;
            panelTransform.X = Math.Max(0, Math.Min(parent.ActualWidth  - panelWidth,  panelTransform.X));
            panelTransform.Y = Math.Max(0, Math.Min(parent.ActualHeight - panelHeight, panelTransform.Y));
            controlPanel.Width  = panelWidth;
            controlPanel.Height = panelHeight;
        }

        private void SavePanelSettings()
        {
            try
            {
                if (string.IsNullOrEmpty(panelSettingsFile)) return;
                string dir = System.IO.Path.GetDirectoryName(panelSettingsFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                double w = controlPanel.ActualWidth  > 0 ? controlPanel.ActualWidth  : panelWidth;
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
                var parts = File.ReadAllText(panelSettingsFile).Split(',');
                if (parts.Length >= 2 && double.TryParse(parts[0], out double x) && double.TryParse(parts[1], out double y))
                { panelTransform.X = x; panelTransform.Y = y; }
                if (parts.Length >= 4 && double.TryParse(parts[2], out double w) && double.TryParse(parts[3], out double h))
                { panelWidth = Math.Max(minPanelWidth, w); panelHeight = Math.Max(minPanelHeight, h); }
            }
            catch { }
        }

        #endregion

        #region Logging

        private void InitializeLogFile()
        {
            try
            {
				string dir = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "log");
//              string dir = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "log");
				if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
//              if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
				logFilePath = System.IO.Path.Combine(dir, $"ActiveNinZaLooker_{DateTime.Now:yyyy-MM-dd}_{chartSessionId}.txt");
//              logFilePath = System.IO.Path.Combine(dir, $"ActiveNinZaLooker_{DateTime.Now:yyyy-MM-dd}_{chartSessionId}.txt");
                logWriter   = new StreamWriter(logFilePath, true) { AutoFlush = true };
                logWriter.WriteLine($"\n=== ActiveNinZaLooker Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                logWriter.WriteLine($"    MinConfluenceToTrade={MinConfluenceToTrade}  MaxBarsAfterSquare={MaxBarsAfterSquare}  CooldownBars={CooldownBars}");
                logWriter.WriteLine($"    Mode: MONITOR ONLY — no orders placed\n");
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

        private void Log(string msg)
        {
            Print(msg);
            if (logWriter == null) return;
            try
            {
                string ts = CurrentBar >= 0
                    ? Time[0].ToString("yyyy-MM-dd HH:mm:ss")
                    : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                logWriter.WriteLine($"{ts} | {msg}");
            }
            catch { }
        }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ActiveNinZaLooker[] cacheActiveNinZaLooker;
		public ActiveNinZaLooker ActiveNinZaLooker(int minConfluenceToTrade, int maxBarsAfterSquare, int minSolarWaveCount, int cooldownBars, bool useRubyRiver, bool useDragonTrend, bool useVIDYAPro, bool useEasyTrend, bool useSolarWave, bool useT3Pro, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, int vIDYAPeriod, int vIDYAVolatilityPeriod, int easyTrendPeriod, int easyTrendATRPeriod, int rubyRiverMAPeriod, double rubyRiverOffsetMultiplier, int rubyRiverOffsetPeriod, int dragonTrendPeriod, int solarWaveATRPeriod, double solarWaveTrendMultiplier, double solarWaveStopMultiplier, bool enableSoundAlert, bool logBarDetails)
		{
			return ActiveNinZaLooker(Input, minConfluenceToTrade, maxBarsAfterSquare, minSolarWaveCount, cooldownBars, useRubyRiver, useDragonTrend, useVIDYAPro, useEasyTrend, useSolarWave, useT3Pro, t3ProPeriod, t3ProTCount, t3ProVFactor, vIDYAPeriod, vIDYAVolatilityPeriod, easyTrendPeriod, easyTrendATRPeriod, rubyRiverMAPeriod, rubyRiverOffsetMultiplier, rubyRiverOffsetPeriod, dragonTrendPeriod, solarWaveATRPeriod, solarWaveTrendMultiplier, solarWaveStopMultiplier, enableSoundAlert, logBarDetails);
		}

		public ActiveNinZaLooker ActiveNinZaLooker(ISeries<double> input, int minConfluenceToTrade, int maxBarsAfterSquare, int minSolarWaveCount, int cooldownBars, bool useRubyRiver, bool useDragonTrend, bool useVIDYAPro, bool useEasyTrend, bool useSolarWave, bool useT3Pro, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, int vIDYAPeriod, int vIDYAVolatilityPeriod, int easyTrendPeriod, int easyTrendATRPeriod, int rubyRiverMAPeriod, double rubyRiverOffsetMultiplier, int rubyRiverOffsetPeriod, int dragonTrendPeriod, int solarWaveATRPeriod, double solarWaveTrendMultiplier, double solarWaveStopMultiplier, bool enableSoundAlert, bool logBarDetails)
		{
			if (cacheActiveNinZaLooker != null)
				for (int idx = 0; idx < cacheActiveNinZaLooker.Length; idx++)
					if (cacheActiveNinZaLooker[idx] != null && cacheActiveNinZaLooker[idx].MinConfluenceToTrade == minConfluenceToTrade && cacheActiveNinZaLooker[idx].MaxBarsAfterSquare == maxBarsAfterSquare && cacheActiveNinZaLooker[idx].MinSolarWaveCount == minSolarWaveCount && cacheActiveNinZaLooker[idx].CooldownBars == cooldownBars && cacheActiveNinZaLooker[idx].UseRubyRiver == useRubyRiver && cacheActiveNinZaLooker[idx].UseDragonTrend == useDragonTrend && cacheActiveNinZaLooker[idx].UseVIDYAPro == useVIDYAPro && cacheActiveNinZaLooker[idx].UseEasyTrend == useEasyTrend && cacheActiveNinZaLooker[idx].UseSolarWave == useSolarWave && cacheActiveNinZaLooker[idx].UseT3Pro == useT3Pro && cacheActiveNinZaLooker[idx].T3ProPeriod == t3ProPeriod && cacheActiveNinZaLooker[idx].T3ProTCount == t3ProTCount && cacheActiveNinZaLooker[idx].T3ProVFactor == t3ProVFactor && cacheActiveNinZaLooker[idx].VIDYAPeriod == vIDYAPeriod && cacheActiveNinZaLooker[idx].VIDYAVolatilityPeriod == vIDYAVolatilityPeriod && cacheActiveNinZaLooker[idx].EasyTrendPeriod == easyTrendPeriod && cacheActiveNinZaLooker[idx].EasyTrendATRPeriod == easyTrendATRPeriod && cacheActiveNinZaLooker[idx].RubyRiverMAPeriod == rubyRiverMAPeriod && cacheActiveNinZaLooker[idx].RubyRiverOffsetMultiplier == rubyRiverOffsetMultiplier && cacheActiveNinZaLooker[idx].RubyRiverOffsetPeriod == rubyRiverOffsetPeriod && cacheActiveNinZaLooker[idx].DragonTrendPeriod == dragonTrendPeriod && cacheActiveNinZaLooker[idx].SolarWaveATRPeriod == solarWaveATRPeriod && cacheActiveNinZaLooker[idx].SolarWaveTrendMultiplier == solarWaveTrendMultiplier && cacheActiveNinZaLooker[idx].SolarWaveStopMultiplier == solarWaveStopMultiplier && cacheActiveNinZaLooker[idx].EnableSoundAlert == enableSoundAlert && cacheActiveNinZaLooker[idx].LogBarDetails == logBarDetails && cacheActiveNinZaLooker[idx].EqualsInput(input))
						return cacheActiveNinZaLooker[idx];
			return CacheIndicator<ActiveNinZaLooker>(new ActiveNinZaLooker(){ MinConfluenceToTrade = minConfluenceToTrade, MaxBarsAfterSquare = maxBarsAfterSquare, MinSolarWaveCount = minSolarWaveCount, CooldownBars = cooldownBars, UseRubyRiver = useRubyRiver, UseDragonTrend = useDragonTrend, UseVIDYAPro = useVIDYAPro, UseEasyTrend = useEasyTrend, UseSolarWave = useSolarWave, UseT3Pro = useT3Pro, T3ProPeriod = t3ProPeriod, T3ProTCount = t3ProTCount, T3ProVFactor = t3ProVFactor, VIDYAPeriod = vIDYAPeriod, VIDYAVolatilityPeriod = vIDYAVolatilityPeriod, EasyTrendPeriod = easyTrendPeriod, EasyTrendATRPeriod = easyTrendATRPeriod, RubyRiverMAPeriod = rubyRiverMAPeriod, RubyRiverOffsetMultiplier = rubyRiverOffsetMultiplier, RubyRiverOffsetPeriod = rubyRiverOffsetPeriod, DragonTrendPeriod = dragonTrendPeriod, SolarWaveATRPeriod = solarWaveATRPeriod, SolarWaveTrendMultiplier = solarWaveTrendMultiplier, SolarWaveStopMultiplier = solarWaveStopMultiplier, EnableSoundAlert = enableSoundAlert, LogBarDetails = logBarDetails }, input, ref cacheActiveNinZaLooker);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ActiveNinZaLooker ActiveNinZaLooker(int minConfluenceToTrade, int maxBarsAfterSquare, int minSolarWaveCount, int cooldownBars, bool useRubyRiver, bool useDragonTrend, bool useVIDYAPro, bool useEasyTrend, bool useSolarWave, bool useT3Pro, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, int vIDYAPeriod, int vIDYAVolatilityPeriod, int easyTrendPeriod, int easyTrendATRPeriod, int rubyRiverMAPeriod, double rubyRiverOffsetMultiplier, int rubyRiverOffsetPeriod, int dragonTrendPeriod, int solarWaveATRPeriod, double solarWaveTrendMultiplier, double solarWaveStopMultiplier, bool enableSoundAlert, bool logBarDetails)
		{
			return indicator.ActiveNinZaLooker(Input, minConfluenceToTrade, maxBarsAfterSquare, minSolarWaveCount, cooldownBars, useRubyRiver, useDragonTrend, useVIDYAPro, useEasyTrend, useSolarWave, useT3Pro, t3ProPeriod, t3ProTCount, t3ProVFactor, vIDYAPeriod, vIDYAVolatilityPeriod, easyTrendPeriod, easyTrendATRPeriod, rubyRiverMAPeriod, rubyRiverOffsetMultiplier, rubyRiverOffsetPeriod, dragonTrendPeriod, solarWaveATRPeriod, solarWaveTrendMultiplier, solarWaveStopMultiplier, enableSoundAlert, logBarDetails);
		}

		public Indicators.ActiveNinZaLooker ActiveNinZaLooker(ISeries<double> input , int minConfluenceToTrade, int maxBarsAfterSquare, int minSolarWaveCount, int cooldownBars, bool useRubyRiver, bool useDragonTrend, bool useVIDYAPro, bool useEasyTrend, bool useSolarWave, bool useT3Pro, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, int vIDYAPeriod, int vIDYAVolatilityPeriod, int easyTrendPeriod, int easyTrendATRPeriod, int rubyRiverMAPeriod, double rubyRiverOffsetMultiplier, int rubyRiverOffsetPeriod, int dragonTrendPeriod, int solarWaveATRPeriod, double solarWaveTrendMultiplier, double solarWaveStopMultiplier, bool enableSoundAlert, bool logBarDetails)
		{
			return indicator.ActiveNinZaLooker(input, minConfluenceToTrade, maxBarsAfterSquare, minSolarWaveCount, cooldownBars, useRubyRiver, useDragonTrend, useVIDYAPro, useEasyTrend, useSolarWave, useT3Pro, t3ProPeriod, t3ProTCount, t3ProVFactor, vIDYAPeriod, vIDYAVolatilityPeriod, easyTrendPeriod, easyTrendATRPeriod, rubyRiverMAPeriod, rubyRiverOffsetMultiplier, rubyRiverOffsetPeriod, dragonTrendPeriod, solarWaveATRPeriod, solarWaveTrendMultiplier, solarWaveStopMultiplier, enableSoundAlert, logBarDetails);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ActiveNinZaLooker ActiveNinZaLooker(int minConfluenceToTrade, int maxBarsAfterSquare, int minSolarWaveCount, int cooldownBars, bool useRubyRiver, bool useDragonTrend, bool useVIDYAPro, bool useEasyTrend, bool useSolarWave, bool useT3Pro, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, int vIDYAPeriod, int vIDYAVolatilityPeriod, int easyTrendPeriod, int easyTrendATRPeriod, int rubyRiverMAPeriod, double rubyRiverOffsetMultiplier, int rubyRiverOffsetPeriod, int dragonTrendPeriod, int solarWaveATRPeriod, double solarWaveTrendMultiplier, double solarWaveStopMultiplier, bool enableSoundAlert, bool logBarDetails)
		{
			return indicator.ActiveNinZaLooker(Input, minConfluenceToTrade, maxBarsAfterSquare, minSolarWaveCount, cooldownBars, useRubyRiver, useDragonTrend, useVIDYAPro, useEasyTrend, useSolarWave, useT3Pro, t3ProPeriod, t3ProTCount, t3ProVFactor, vIDYAPeriod, vIDYAVolatilityPeriod, easyTrendPeriod, easyTrendATRPeriod, rubyRiverMAPeriod, rubyRiverOffsetMultiplier, rubyRiverOffsetPeriod, dragonTrendPeriod, solarWaveATRPeriod, solarWaveTrendMultiplier, solarWaveStopMultiplier, enableSoundAlert, logBarDetails);
		}

		public Indicators.ActiveNinZaLooker ActiveNinZaLooker(ISeries<double> input , int minConfluenceToTrade, int maxBarsAfterSquare, int minSolarWaveCount, int cooldownBars, bool useRubyRiver, bool useDragonTrend, bool useVIDYAPro, bool useEasyTrend, bool useSolarWave, bool useT3Pro, int t3ProPeriod, int t3ProTCount, double t3ProVFactor, int vIDYAPeriod, int vIDYAVolatilityPeriod, int easyTrendPeriod, int easyTrendATRPeriod, int rubyRiverMAPeriod, double rubyRiverOffsetMultiplier, int rubyRiverOffsetPeriod, int dragonTrendPeriod, int solarWaveATRPeriod, double solarWaveTrendMultiplier, double solarWaveStopMultiplier, bool enableSoundAlert, bool logBarDetails)
		{
			return indicator.ActiveNinZaLooker(input, minConfluenceToTrade, maxBarsAfterSquare, minSolarWaveCount, cooldownBars, useRubyRiver, useDragonTrend, useVIDYAPro, useEasyTrend, useSolarWave, useT3Pro, t3ProPeriod, t3ProTCount, t3ProVFactor, vIDYAPeriod, vIDYAVolatilityPeriod, easyTrendPeriod, easyTrendATRPeriod, rubyRiverMAPeriod, rubyRiverOffsetMultiplier, rubyRiverOffsetPeriod, dragonTrendPeriod, solarWaveATRPeriod, solarWaveTrendMultiplier, solarWaveStopMultiplier, enableSoundAlert, logBarDetails);
		}
	}
}

#endregion
