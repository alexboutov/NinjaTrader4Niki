#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

public enum DragonTrendMAType
{
    SMA,
    EMA,
    WMA,
    DEMA,
    TEMA
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class DragonTrendEquivalent : Indicator
    {
        #region Private Variables
        private double prevSignal;
        private double momentum;
        private double smoothedMomentum;
        private bool isUptrend;
        
        // For momentum smoothing
        private double emaState;
        private double ema1State, ema2State;
        private double ema1TState, ema2TState, ema3TState;
        
        // For secondary smoothing
        private double smoothEmaState;
        private double smoothEma1State, smoothEma2State;
        private double smoothEma1TState, smoothEma2TState, smoothEma3TState;
        
        // Circular buffer
        private double[] momentumBuffer;
        private double[] smoothBuffer;
        private int momIndex;
        private int smoothIndex;
        
        private bool isInitialized;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Dragon Trend Equivalent - Momentum-based trend indicator";
                Name = "DragonTrendEquivalent";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                
                // Default parameters matching ninZaDragonTrend
                Period = 10;
                SmoothingEnabled = true;
                SmoothingMethod = DragonTrendMAType.EMA;
                SmoothingPeriod = 5;
                
                // Visual settings
                ShowMarkers = true;
                UptrendMarker = "▲";
                DowntrendMarker = "▼";
                MarkerOffset = 10;
                
                // No plot - Dragon Trend is overlay but doesn't draw lines
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                // Initialize arrays
                momentumBuffer = new double[Period + 1];
                smoothBuffer = new double[SmoothingPeriod + 1];
                
                momIndex = 0;
                smoothIndex = 0;
                isInitialized = false;
                isUptrend = false;
                prevSignal = 0;
                
                // Initialize EMA states
                emaState = 0;
                smoothEmaState = 0;
                ema1State = ema2State = 0;
                smoothEma1State = smoothEma2State = 0;
                ema1TState = ema2TState = ema3TState = 0;
                smoothEma1TState = smoothEma2TState = smoothEma3TState = 0;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Period)
            {
                prevSignal = 0;
                return;
            }

            // Calculate momentum (rate of change over Period bars)
            // Dragon Trend appears to use a momentum/directional calculation
            momentum = Close[0] - Close[Period];
            
            // Store in buffer
            momentumBuffer[momIndex] = momentum;
            momIndex = (momIndex + 1) % momentumBuffer.Length;
            
            // Calculate smoothed momentum using EMA of momentum values
            double smoothedMom = CalculateMA(momentum, DragonTrendMAType.EMA, Period, 
                                             ref emaState, ref ema1State, ref ema2State,
                                             ref ema1TState, ref ema2TState, ref ema3TState,
                                             momentumBuffer, ref momIndex);
            
            // Apply secondary smoothing if enabled
            if (SmoothingEnabled)
            {
                smoothedMomentum = CalculateMA(smoothedMom, SmoothingMethod, SmoothingPeriod,
                                               ref smoothEmaState, ref smoothEma1State, ref smoothEma2State,
                                               ref smoothEma1TState, ref smoothEma2TState, ref smoothEma3TState,
                                               smoothBuffer, ref smoothIndex);
                
                smoothBuffer[smoothIndex] = smoothedMom;
                smoothIndex = (smoothIndex + 1) % smoothBuffer.Length;
            }
            else
            {
                smoothedMomentum = smoothedMom;
            }
            
            // Determine signal value
            // prevSignal > 0 = uptrend, < 0 = downtrend
            double previousSignal = prevSignal;
            prevSignal = smoothedMomentum;
            
            // Determine trend
            bool previousTrend = isUptrend;
            
            if (!isInitialized && CurrentBar >= Period + SmoothingPeriod)
            {
                isUptrend = prevSignal > 0;
                isInitialized = true;
            }
            else if (isInitialized)
            {
                // Trend changes when signal crosses zero
                if (prevSignal > 0 && previousSignal <= 0)
                    isUptrend = true;
                else if (prevSignal < 0 && previousSignal >= 0)
                    isUptrend = false;
            }
            
            // Draw markers on trend change
            if (ShowMarkers && isInitialized && isUptrend != previousTrend)
            {
                if (isUptrend)
                {
                    Draw.Text(this, "Up" + CurrentBar, UptrendMarker, 0, Low[0] - MarkerOffset * TickSize, Brushes.DodgerBlue);
                }
                else
                {
                    Draw.Text(this, "Down" + CurrentBar, DowntrendMarker, 0, High[0] + MarkerOffset * TickSize, Brushes.HotPink);
                }
            }
        }
        
        private double CalculateMA(double value, DragonTrendMAType maType, int period,
                                   ref double ema, ref double ema1, ref double ema2,
                                   ref double ema1T, ref double ema2T, ref double ema3T,
                                   double[] buffer, ref int bufferIndex)
        {
            double k = 2.0 / (period + 1);
            
            switch (maType)
            {
                case DragonTrendMAType.SMA:
                    if (CurrentBar < period)
                    {
                        double sum = 0;
                        int count = Math.Min(CurrentBar + 1, period);
                        for (int i = 0; i < count; i++)
                            sum += buffer[i];
                        return (sum + value) / (count + 1);
                    }
                    else
                    {
                        double sum = 0;
                        for (int i = 0; i < period; i++)
                            sum += buffer[(bufferIndex + i) % buffer.Length];
                        return sum / period;
                    }
                    
                case DragonTrendMAType.EMA:
                    if (CurrentBar < period)
                    {
                        ema = (ema * CurrentBar + value) / (CurrentBar + 1);
                    }
                    else
                    {
                        ema = (value - ema) * k + ema;
                    }
                    return ema;
                    
                case DragonTrendMAType.WMA:
                    {
                        int count = Math.Min(CurrentBar + 1, period);
                        double sum = 0;
                        double weightSum = 0;
                        int weight = 1;
                        for (int i = 0; i < count; i++)
                        {
                            sum += buffer[(bufferIndex + i) % buffer.Length] * weight;
                            weightSum += weight;
                            weight++;
                        }
                        return sum / weightSum;
                    }
                    
                case DragonTrendMAType.DEMA:
                    if (CurrentBar < period)
                    {
                        ema1 = (ema1 * CurrentBar + value) / (CurrentBar + 1);
                        ema2 = (ema2 * CurrentBar + ema1) / (CurrentBar + 1);
                    }
                    else
                    {
                        ema1 = (value - ema1) * k + ema1;
                        ema2 = (ema1 - ema2) * k + ema2;
                    }
                    return 2 * ema1 - ema2;
                    
                case DragonTrendMAType.TEMA:
                    if (CurrentBar < period)
                    {
                        ema1T = (ema1T * CurrentBar + value) / (CurrentBar + 1);
                        ema2T = (ema2T * CurrentBar + ema1T) / (CurrentBar + 1);
                        ema3T = (ema3T * CurrentBar + ema2T) / (CurrentBar + 1);
                    }
                    else
                    {
                        ema1T = (value - ema1T) * k + ema1T;
                        ema2T = (ema1T - ema2T) * k + ema2T;
                        ema3T = (ema2T - ema3T) * k + ema3T;
                    }
                    return 3 * ema1T - 3 * ema2T + ema3T;
                    
                default:
                    return value;
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Period", Description = "Momentum lookback period", Order = 1, GroupName = "1. Parameters")]
        public int Period { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Smoothing Enabled", Description = "Enable additional smoothing", Order = 2, GroupName = "2. Smoothing")]
        public bool SmoothingEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Smoothing Method", Description = "Type of smoothing to apply", Order = 3, GroupName = "2. Smoothing")]
        public DragonTrendMAType SmoothingMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Smoothing Period", Description = "Period for smoothing", Order = 4, GroupName = "2. Smoothing")]
        public int SmoothingPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Markers", Description = "Show trend change markers", Order = 5, GroupName = "3. Visual")]
        public bool ShowMarkers { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Uptrend Marker", Description = "Symbol for uptrend", Order = 6, GroupName = "3. Visual")]
        public string UptrendMarker { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Downtrend Marker", Description = "Symbol for downtrend", Order = 7, GroupName = "3. Visual")]
        public string DowntrendMarker { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Marker Offset", Description = "Offset in ticks for markers", Order = 8, GroupName = "3. Visual")]
        public int MarkerOffset { get; set; }

        // Public properties for strategy access
        [Browsable(false)]
        public bool IsUptrend
        {
            get { return isUptrend; }
        }
        
        [Browsable(false)]
        public double PrevSignal
        {
            get { return prevSignal; }
        }
        
        [Browsable(false)]
        public double Momentum
        {
            get { return smoothedMomentum; }
        }
        #endregion
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private DragonTrendEquivalent[] cacheDragonTrendEquivalent;
		public DragonTrendEquivalent DragonTrendEquivalent(int period, bool smoothingEnabled, DragonTrendMAType smoothingMethod, int smoothingPeriod, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return DragonTrendEquivalent(Input, period, smoothingEnabled, smoothingMethod, smoothingPeriod, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}

		public DragonTrendEquivalent DragonTrendEquivalent(ISeries<double> input, int period, bool smoothingEnabled, DragonTrendMAType smoothingMethod, int smoothingPeriod, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			if (cacheDragonTrendEquivalent != null)
				for (int idx = 0; idx < cacheDragonTrendEquivalent.Length; idx++)
					if (cacheDragonTrendEquivalent[idx] != null && cacheDragonTrendEquivalent[idx].Period == period && cacheDragonTrendEquivalent[idx].SmoothingEnabled == smoothingEnabled && cacheDragonTrendEquivalent[idx].SmoothingMethod == smoothingMethod && cacheDragonTrendEquivalent[idx].SmoothingPeriod == smoothingPeriod && cacheDragonTrendEquivalent[idx].ShowMarkers == showMarkers && cacheDragonTrendEquivalent[idx].UptrendMarker == uptrendMarker && cacheDragonTrendEquivalent[idx].DowntrendMarker == downtrendMarker && cacheDragonTrendEquivalent[idx].MarkerOffset == markerOffset && cacheDragonTrendEquivalent[idx].EqualsInput(input))
						return cacheDragonTrendEquivalent[idx];
			return CacheIndicator<DragonTrendEquivalent>(new DragonTrendEquivalent(){ Period = period, SmoothingEnabled = smoothingEnabled, SmoothingMethod = smoothingMethod, SmoothingPeriod = smoothingPeriod, ShowMarkers = showMarkers, UptrendMarker = uptrendMarker, DowntrendMarker = downtrendMarker, MarkerOffset = markerOffset }, input, ref cacheDragonTrendEquivalent);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.DragonTrendEquivalent DragonTrendEquivalent(int period, bool smoothingEnabled, DragonTrendMAType smoothingMethod, int smoothingPeriod, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.DragonTrendEquivalent(Input, period, smoothingEnabled, smoothingMethod, smoothingPeriod, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}

		public Indicators.DragonTrendEquivalent DragonTrendEquivalent(ISeries<double> input , int period, bool smoothingEnabled, DragonTrendMAType smoothingMethod, int smoothingPeriod, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.DragonTrendEquivalent(input, period, smoothingEnabled, smoothingMethod, smoothingPeriod, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.DragonTrendEquivalent DragonTrendEquivalent(int period, bool smoothingEnabled, DragonTrendMAType smoothingMethod, int smoothingPeriod, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.DragonTrendEquivalent(Input, period, smoothingEnabled, smoothingMethod, smoothingPeriod, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}

		public Indicators.DragonTrendEquivalent DragonTrendEquivalent(ISeries<double> input , int period, bool smoothingEnabled, DragonTrendMAType smoothingMethod, int smoothingPeriod, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.DragonTrendEquivalent(input, period, smoothingEnabled, smoothingMethod, smoothingPeriod, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}
	}
}

#endregion
