#region Using declarations
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
#endregion

// ═══════════════════════════════════════════════════════════════════════════════
//  ActiveNinZaLooker — split across 6 partial-class files:
//    ANZ_Main.cs         fields, OnStateChange, OnBarUpdate, FireSignal
//    ANZ_Parameters.cs   [NinjaScriptProperty] declarations
//    ANZ_Indicators.cs   indicator loading, accessors, confluence helpers
//    ANZ_Panel.cs        panel creation, refresh, checkboxes
//    ANZ_DragResize.cs   drag / resize / settings persistence
//    ANZ_Logging.cs      log file helpers
// ═══════════════════════════════════════════════════════════════════════════════

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// ActiveNinZaLooker — Signal monitor for the ActiveNiki confluence system.
    /// Mirrors ActiveNikiTrader signal detection as a pure Indicator (never places orders).
    /// Panel shows WATCHING (live confluence) or SUBMIT LONG/SHORT (price + direction).
    /// 6 confluence indicators: RR, DT, VY, ET, SW, T3P.
    /// </summary>
    public partial class ActiveNinZaLooker : Indicator
    {
        #region Fields

        // ── ninZa licensed indicator references ──────────────────────────────
        private object rubyRiver, vidyaPro, easyTrend, dragonTrend, solarWave, ninZaT3Pro;
        private FieldInfo rrIsUptrend, vyIsUptrend, etIsUptrend, dtPrevSignal,
                          swIsUptrend, swCountWave, t3pIsUptrend;

        // ── Native AIQ_1 ─────────────────────────────────────────────────────
        private object    nativeAiq1;
        private FieldInfo nativeAiq1TrendState;
        private bool      useNativeAiq1;

        // ── Chart-attached equivalent indicator references ────────────────────
        private object chartAiq1Equivalent;
        private object chartRubyRiverEquiv, chartDragonTrendEquiv, chartVidyaProEquiv;
        private object chartEasyTrendEquiv, chartSolarWaveEquiv, chartT3ProEquiv;

        private System.Windows.Controls.CheckBox cbRR, cbDT, cbVY, cbET, cbSW, cbT3P;

        private System.Reflection.PropertyInfo aiq1IsUptrend;
        private System.Reflection.PropertyInfo rrEquivIsUptrend, dtEquivPrevSignal, vyEquivIsUptrend;
        private System.Reflection.PropertyInfo etEquivIsUptrend, swEquivIsUptrend, swEquivCountWave, t3pEquivIsUptrend;

        private bool useChartAiq1;
        private bool useChartRR, useChartDT, useChartVY, useChartET, useChartSW, useChartT3P;

        // ── Hosted (fallback) equivalent indicators ──────────────────────────
        private T3ProEquivalent       t3ProEquivalent;
        private VIDYAProEquivalent    vidyaProEquivalent;
        private EasyTrendEquivalent   easyTrendEquivalent;
        private RubyRiverEquivalent   rubyRiverEquivalent;
        private DragonTrendEquivalent dragonTrendEquivalent;
        private SolarWaveEquivalent   solarWaveEquivalent;
        private AIQ_1Equivalent       aiq1Equivalent;

        private bool useHostedRR, useHostedDT, useHostedVY, useHostedET, useHostedSW, useHostedT3P;
        private bool indicatorsReady;

        // ── Runtime toggle state (driven by panel checkboxes) ─────────────────
        // These shadow the parameter booleans and are flipped by the checkboxes.
        private bool rtUseRR, rtUseDT, rtUseVY, rtUseET, rtUseSW, rtUseT3P;

        // ── Trigger / window tracking ─────────────────────────────────────────
        private int      barsSinceYellowSquare = -1;
        private int      barsSinceOrangeSquare = -1;
        private int      barsSinceLastSignal   = -1;
        private DateTime lastSignalTime        = DateTime.MinValue;
        private bool     prevAIQ1_IsUp;
        private bool     isFirstBar = true;

        // ── Previous-bar state for flip detection ─────────────────────────────
        private bool prevRR_IsUp, prevDT_IsUp, prevVY_IsUp, prevET_IsUp, prevSW_IsUp, prevT3P_IsUp;

        // ── Panel state ────────────────────────────────────────────────────────
        private enum PanelMode { Watching, Submit }
        private PanelMode currentMode = PanelMode.Watching;

        // Last qualifying signal info (shown in Submit mode)
        private string   submitDirection = "";
        private double   submitPrice     = 0;
        private DateTime submitTime      = DateTime.MinValue;
        private int      submitBull, submitBear, submitTotal;

        // ── Panel UI ──────────────────────────────────────────────────────────
        private Grid  controlPanel;
        private bool  panelActive;
        private bool  isDragging;
        private bool  isResizing;
        private Point dragStartPoint;
        private double resizeStartWidth, resizeStartHeight;
        private Point  resizeStartMousePos;
        private double resizeStartLeft, resizeStartTop;
        private System.Windows.Media.TranslateTransform panelTransform;
        private string panelSettingsFile;

        private enum ResizeEdge { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }
        private ResizeEdge currentResizeEdge = ResizeEdge.None;
        private const double EdgeThreshold  = 8;
        private double panelWidth    = 230;
        private double panelHeight   = 340;
        private double minPanelWidth  = 180;
        private double minPanelHeight = 260;

        // Panel labels
        private TextBlock lblMode;
        private TextBlock lblDirectionPrice;
        private TextBlock lblWindow;
        private TextBlock lblConfluence;
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

        #region OnStateChange

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                SetIndicatorDefaults();
            }
            else if (State == State.DataLoaded)
            {
                chartSessionId = DateTime.Now.ToString("HHmmss") + "_" + new Random().Next(1000, 9999);
                InitializeLogFile();

                // Initialise runtime toggles from parameters
                rtUseRR  = UseRubyRiver;
                rtUseDT  = UseDragonTrend;
                rtUseVY  = UseVIDYAPro;
                rtUseET  = UseEasyTrend;
                rtUseSW  = UseSolarWave;
                rtUseT3P = UseT3Pro;

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
                // Reset panel to Watching so partner sees a fresh window opened
                currentMode = PanelMode.Watching;
                PlayWindowAlert();
                Log(inCooldown
                    ? $"🟨 Yellow ■ @ {barTime:HH:mm:ss} | BLOCKED by cooldown ({barsSinceLastSignal}/{CooldownBars})"
                    : $"🟨 Yellow ■ @ {barTime:HH:mm:ss} | LONG window OPEN");
            }
            else if (orangeAppeared)
            {
                barsSinceOrangeSquare = 0;
                barsSinceYellowSquare = -1;
                currentMode = PanelMode.Watching;
                PlayWindowAlert();
                Log(inCooldown
                    ? $"🟧 Orange ■ @ {barTime:HH:mm:ss} | BLOCKED by cooldown ({barsSinceLastSignal}/{CooldownBars})"
                    : $"🟧 Orange ■ @ {barTime:HH:mm:ss} | SHORT window OPEN");
            }
            else
            {
                // Advance and expire open windows
                if (barsSinceYellowSquare >= 0)
                {
                    barsSinceYellowSquare++;
                    if (barsSinceYellowSquare > MaxBarsAfterSquare)
                    {
                        Log($"LONG window EXPIRED @ {barTime:HH:mm:ss} (>{MaxBarsAfterSquare} bars)");
                        barsSinceYellowSquare = -1;
                        // Return panel to Watching if no new signal replaced it
                        if (currentMode == PanelMode.Watching)
                            RefreshPanel();
                    }
                }
                if (barsSinceOrangeSquare >= 0)
                {
                    barsSinceOrangeSquare++;
                    if (barsSinceOrangeSquare > MaxBarsAfterSquare)
                    {
                        Log($"SHORT window EXPIRED @ {barTime:HH:mm:ss} (>{MaxBarsAfterSquare} bars)");
                        barsSinceOrangeSquare = -1;
                        if (currentMode == PanelMode.Watching)
                            RefreshPanel();
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
            // Window is bars 0..MaxBarsAfterSquare inclusive (MaxBarsAfterSquare+1 bars total)
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

        // Softer alert when the AIQ1 square first appears (window opens)
        private void PlayWindowAlert()
        {
            if (EnableSoundAlert)
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
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

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ActiveNinZaLooker[] cacheActiveNinZaLooker;
		public ActiveNinZaLooker ActiveNinZaLooker()
		{
			return ActiveNinZaLooker(Input);
		}

		public ActiveNinZaLooker ActiveNinZaLooker(ISeries<double> input)
		{
			if (cacheActiveNinZaLooker != null)
				for (int idx = 0; idx < cacheActiveNinZaLooker.Length; idx++)
					if (cacheActiveNinZaLooker[idx] != null &&  cacheActiveNinZaLooker[idx].EqualsInput(input))
						return cacheActiveNinZaLooker[idx];
			return CacheIndicator<ActiveNinZaLooker>(new ActiveNinZaLooker(), input, ref cacheActiveNinZaLooker);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ActiveNinZaLooker ActiveNinZaLooker()
		{
			return indicator.ActiveNinZaLooker(Input);
		}

		public Indicators.ActiveNinZaLooker ActiveNinZaLooker(ISeries<double> input )
		{
			return indicator.ActiveNinZaLooker(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ActiveNinZaLooker ActiveNinZaLooker()
		{
			return indicator.ActiveNinZaLooker(Input);
		}

		public Indicators.ActiveNinZaLooker ActiveNinZaLooker(ISeries<double> input )
		{
			return indicator.ActiveNinZaLooker(input);
		}
	}
}

#endregion
