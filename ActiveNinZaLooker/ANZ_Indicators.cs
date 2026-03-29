using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Controls;
using NinjaTrader.NinjaScript.Indicators;

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class ActiveNinZaLooker : Indicator
    {
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
                        nativeAiq1           = ind;
                        nativeAiq1TrendState  = t.GetField("trendState", priv);
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
            Log($"  AIQ_1      : {(useNativeAiq1 ? "NATIVE" : useChartAiq1 ? "CHART" : "HOSTED")}");
            Log($"  RubyRiver  : {SrcLabel(rubyRiver   != null, useChartRR)}");
            Log($"  DragonTrend: {SrcLabel(dragonTrend != null, useChartDT)}");
            Log($"  VIDYAPro   : {SrcLabel(vidyaPro    != null, useChartVY)}");
            Log($"  EasyTrend  : {SrcLabel(easyTrend   != null, useChartET)}");
            Log($"  SolarWave  : {SrcLabel(solarWave   != null, useChartSW)}");
            Log($"  T3Pro      : {SrcLabel(ninZaT3Pro  != null, useChartT3P)}");
            Log("---------------------------");
        }

        #endregion

        #region Indicator Accessors

        // Helpers to safely read private fields / public properties via reflection
        private bool   GF(object o, FieldInfo    f) { try { return o != null && f != null && (bool)f.GetValue(o); } catch { return false; } }
        private double GD(object o, FieldInfo    f) { try { return o != null && f != null ? (double)f.GetValue(o) : 0; } catch { return 0; } }
        private int    GI(object o, FieldInfo    f) { try { return o != null && f != null ? (int)f.GetValue(o)   : 0; } catch { return 0; } }
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

            if (rtUseRR)  { total++; if (RR_IsUp)                                    bull++; else bear++; }
            if (rtUseDT)  { total++; if (DT_IsUp)                                    bull++; else if (DT_IsDown) bear++; }
            if (rtUseVY)  { total++; if (VY_IsUp)                                    bull++; else bear++; }
            if (rtUseET)  { total++; if (ET_IsUp)                                    bull++; else bear++; }
            if (rtUseSW)  { total++; if (SW_IsUp && SW_Count >= MinSolarWaveCount)   bull++; else if (!SW_IsUp && SW_Count <= -MinSolarWaveCount) bear++; }
            if (rtUseT3P) { total++; if (T3P_IsUp)                                   bull++; else bear++; }

            return (bull, bear, total);
        }

        // Returns the name of the first indicator that confirms the direction.
        // Prefers a fresh flip (this bar) over already-in-state.
        private string FindBullishConfirmation()
        {
            if (rtUseRR  && RR_IsUp  && !prevRR_IsUp)  return "RR";
            if (rtUseDT  && DT_IsUp  && !prevDT_IsUp)  return "DT";
            if (rtUseVY  && VY_IsUp  && !prevVY_IsUp)  return "VY";
            if (rtUseET  && ET_IsUp  && !prevET_IsUp)   return "ET";
            if (rtUseSW  && SW_IsUp  && !prevSW_IsUp)   return "SW";
            if (rtUseT3P && T3P_IsUp && !prevT3P_IsUp)  return "T3P";
            // already aligned
            if (rtUseRR  && RR_IsUp)  return "RR";
            if (rtUseDT  && DT_IsUp)  return "DT";
            if (rtUseVY  && VY_IsUp)  return "VY";
            if (rtUseET  && ET_IsUp)  return "ET";
            if (rtUseSW  && SW_IsUp)  return "SW";
            if (rtUseT3P && T3P_IsUp) return "T3P";
            return null;
        }

        private string FindBearishConfirmation()
        {
            if (rtUseRR  && !RR_IsUp  && prevRR_IsUp)  return "RR";
            if (rtUseDT  && DT_IsDown && prevDT_IsUp)  return "DT";
            if (rtUseVY  && !VY_IsUp  && prevVY_IsUp)  return "VY";
            if (rtUseET  && !ET_IsUp  && prevET_IsUp)  return "ET";
            if (rtUseSW  && !SW_IsUp  && prevSW_IsUp)  return "SW";
            if (rtUseT3P && !T3P_IsUp && prevT3P_IsUp) return "T3P";
            // already aligned
            if (rtUseRR  && !RR_IsUp)  return "RR";
            if (rtUseDT  && DT_IsDown) return "DT";
            if (rtUseVY  && !VY_IsUp)  return "VY";
            if (rtUseET  && !ET_IsUp)  return "ET";
            if (rtUseSW  && !SW_IsUp)  return "SW";
            if (rtUseT3P && !T3P_IsUp) return "T3P";
            return null;
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
