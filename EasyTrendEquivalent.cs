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

public enum EasyTrendMAType
{
    SMA,
    EMA,
    WMA,
    DEMA,
    TEMA
}

public enum EasyTrendFilterUnit
{
    ninZaATR,
    Ticks,
    Points
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class EasyTrendEquivalent : Indicator
    {
        #region Private Variables
        private double maValue;
        private double smoothedValue;
        private double previousSmoothedValue;
        private bool isUptrend;
        private double upperBand;
        private double lowerBand;
        
        // For MA calculation
        private double[] maBuffer;
        private int maIndex;
        
        // For smoothing
        private double[] smoothingBuffer;
        private int smoothingIndex;
        
        // For ATR
        private double[] trueRanges;
        private int trIndex;
        private double atrValue;
        
        // EMA state variables
        private double emaState;
        private double smoothEmaState;
        
        // DEMA state variables
        private double ema1State, ema2State;
        private double smoothEma1State, smoothEma2State;
        
        // TEMA state variables
        private double ema1TState, ema2TState, ema3TState;
        private double smoothEma1TState, smoothEma2TState, smoothEma3TState;
        
        private bool isInitialized;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Easy Trend Equivalent - Moving Average with trend detection";
                Name = "EasyTrendEquivalent";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                
                // Default parameters matching ninZaEasyTrend
                MAType = EasyTrendMAType.EMA;
                Period = 30;
                SmoothingEnabled = true;
                SmoothingMethod = EasyTrendMAType.EMA;
                SmoothingPeriod = 7;
                FilterEnabled = true;
                FilterAfterSmoothing = true;
                FilterMultiplier = 0.5;
                FilterUnit = EasyTrendFilterUnit.ninZaATR;
                FilterATRPeriod = 100;
                
                // Visual settings
                ShowPlot = true;
                ShowMarkers = false;
                UptrendMarker = "▲ + Easy";
                DowntrendMarker = "Easy + ▼";
                MarkerOffset = 10;
                
                AddPlot(new Stroke(Brushes.Blue, DashStyleHelper.Dot, 3), PlotStyle.Dot, "Moving Average");
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                // Initialize arrays
                maBuffer = new double[Period];
                smoothingBuffer = new double[SmoothingPeriod];
                trueRanges = new double[FilterATRPeriod];
                
                maIndex = 0;
                smoothingIndex = 0;
                trIndex = 0;
                isInitialized = false;
                isUptrend = false;
                
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
            if (CurrentBar < 1)
            {
                maValue = Close[0];
                smoothedValue = Close[0];
                previousSmoothedValue = Close[0];
                Value[0] = Close[0];
                emaState = Close[0];
                smoothEmaState = Close[0];
                ema1State = ema2State = Close[0];
                smoothEma1State = smoothEma2State = Close[0];
                ema1TState = ema2TState = ema3TState = Close[0];
                smoothEma1TState = smoothEma2TState = smoothEma3TState = Close[0];
                return;
            }

            // Calculate True Range for ATR
            double trueRange = Math.Max(High[0] - Low[0], 
                              Math.Max(Math.Abs(High[0] - Close[1]), Math.Abs(Low[0] - Close[1])));
            
            // Update TR circular buffer
            trueRanges[trIndex] = trueRange;
            trIndex = (trIndex + 1) % FilterATRPeriod;
            
            // Calculate ATR
            if (CurrentBar >= FilterATRPeriod)
            {
                atrValue = 0;
                for (int i = 0; i < FilterATRPeriod; i++)
                    atrValue += trueRanges[i];
                atrValue /= FilterATRPeriod;
            }
            else
            {
                // Use available data for ATR
                double sum = 0;
                int count = Math.Min(CurrentBar + 1, FilterATRPeriod);
                for (int i = 0; i < count; i++)
                    sum += trueRanges[i];
                atrValue = sum / count;
            }

            // Calculate main MA
            maValue = CalculateMA(Close[0], MAType, Period, ref emaState, ref ema1State, ref ema2State, ref ema1TState, ref ema2TState, ref ema3TState, maBuffer, ref maIndex);
            
            // Store in MA buffer for SMA/WMA
            maBuffer[maIndex] = Close[0];
            maIndex = (maIndex + 1) % Period;
            
            // Apply smoothing if enabled
            double outputValue;
            if (SmoothingEnabled)
            {
                outputValue = CalculateMA(maValue, SmoothingMethod, SmoothingPeriod, ref smoothEmaState, ref smoothEma1State, ref smoothEma2State, ref smoothEma1TState, ref smoothEma2TState, ref smoothEma3TState, smoothingBuffer, ref smoothingIndex);
                
                // Store in smoothing buffer
                smoothingBuffer[smoothingIndex] = maValue;
                smoothingIndex = (smoothingIndex + 1) % SmoothingPeriod;
            }
            else
            {
                outputValue = maValue;
            }
            
            // Store previous for trend detection
            previousSmoothedValue = smoothedValue;
            smoothedValue = outputValue;
            
            // Set plot value
            Value[0] = outputValue;
            
            // Calculate filter distance
            double filterDistance = 0;
            if (FilterEnabled)
            {
                switch (FilterUnit)
                {
                    case EasyTrendFilterUnit.ninZaATR:
                        filterDistance = FilterMultiplier * atrValue;
                        break;
                    case EasyTrendFilterUnit.Ticks:
                        filterDistance = FilterMultiplier * TickSize;
                        break;
                    case EasyTrendFilterUnit.Points:
                        filterDistance = FilterMultiplier;
                        break;
                }
            }
            
            // Calculate bands based on FilterAfterSmoothing setting
            double bandBase = FilterAfterSmoothing ? outputValue : maValue;
            upperBand = bandBase + filterDistance;
            lowerBand = bandBase - filterDistance;
            
            // Determine trend with filter
            bool previousTrend = isUptrend;
            
            if (CurrentBar >= Period + 1)
            {
                if (!isInitialized)
                {
                    // Initialize trend based on price vs MA
                    isUptrend = Close[0] > outputValue;
                    isInitialized = true;
                }
                else if (FilterEnabled)
                {
                    // With filter: price must cross band to flip trend
                    if (!isUptrend && Close[0] > upperBand)
                        isUptrend = true;
                    else if (isUptrend && Close[0] < lowerBand)
                        isUptrend = false;
                }
                else
                {
                    // Without filter: simple price vs MA comparison
                    isUptrend = Close[0] > outputValue;
                }
            }
            
            // Update plot color based on trend
            if (ShowPlot)
            {
                PlotBrushes[0][0] = isUptrend ? Brushes.Blue : Brushes.Crimson;
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
        
        private double CalculateMA(double value, EasyTrendMAType maType, int period, ref double ema, ref double ema1, ref double ema2, ref double ema1T, ref double ema2T, ref double ema3T, double[] buffer, ref int bufferIndex)
        {
            double k = 2.0 / (period + 1);
            
            switch (maType)
            {
                case EasyTrendMAType.SMA:
                    if (CurrentBar < period)
                    {
                        double sum = 0;
                        int count = Math.Min(CurrentBar + 1, period);
                        for (int i = 0; i < count; i++)
                            sum += buffer[i];
                        sum += value; // Include current value
                        return sum / (count + 1);
                    }
                    else
                    {
                        double sum = 0;
                        for (int i = 0; i < period; i++)
                            sum += buffer[i];
                        return sum / period;
                    }
                    
                case EasyTrendMAType.EMA:
                    if (CurrentBar < period)
                    {
                        // Use SMA for warmup
                        ema = (ema * CurrentBar + value) / (CurrentBar + 1);
                    }
                    else
                    {
                        ema = (value - ema) * k + ema;
                    }
                    return ema;
                    
                case EasyTrendMAType.WMA:
                    if (CurrentBar < period)
                    {
                        double sum = 0;
                        double weightSum = 0;
                        int weight = 1;
                        for (int i = 0; i <= CurrentBar; i++)
                        {
                            sum += (i < CurrentBar ? buffer[i] : value) * weight;
                            weightSum += weight;
                            weight++;
                        }
                        return sum / weightSum;
                    }
                    else
                    {
                        double sum = 0;
                        double weightSum = 0;
                        int weight = 1;
                        for (int i = 0; i < period; i++)
                        {
                            int idx = (bufferIndex + i) % period;
                            sum += buffer[idx] * weight;
                            weightSum += weight;
                            weight++;
                        }
                        return sum / weightSum;
                    }
                    
                case EasyTrendMAType.DEMA:
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
                    
                case EasyTrendMAType.TEMA:
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
        [Display(Name = "MA Type", Description = "Type of moving average", Order = 1, GroupName = "1. Moving Average")]
        public EasyTrendMAType MAType { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Period", Description = "MA period", Order = 2, GroupName = "1. Moving Average")]
        public int Period { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Smoothing Enabled", Description = "Enable additional smoothing", Order = 3, GroupName = "2. Smoothing")]
        public bool SmoothingEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Smoothing Method", Description = "Type of smoothing to apply", Order = 4, GroupName = "2. Smoothing")]
        public EasyTrendMAType SmoothingMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Smoothing Period", Description = "Period for smoothing", Order = 5, GroupName = "2. Smoothing")]
        public int SmoothingPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filter Enabled", Description = "Enable trend filter", Order = 6, GroupName = "3. Filter")]
        public bool FilterEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filter After Smoothing", Description = "Apply filter after smoothing", Order = 7, GroupName = "3. Filter")]
        public bool FilterAfterSmoothing { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Filter Multiplier", Description = "Filter multiplier", Order = 8, GroupName = "3. Filter")]
        public double FilterMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filter Unit", Description = "Unit for filter calculation", Order = 9, GroupName = "3. Filter")]
        public EasyTrendFilterUnit FilterUnit { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Filter ATR Period", Description = "Period for ATR calculation", Order = 10, GroupName = "3. Filter")]
        public int FilterATRPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Plot", Description = "Show MA line on chart", Order = 11, GroupName = "4. Visual")]
        public bool ShowPlot { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Markers", Description = "Show trend change markers", Order = 12, GroupName = "4. Visual")]
        public bool ShowMarkers { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Uptrend Marker", Description = "Symbol for uptrend", Order = 13, GroupName = "4. Visual")]
        public string UptrendMarker { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Downtrend Marker", Description = "Symbol for downtrend", Order = 14, GroupName = "4. Visual")]
        public string DowntrendMarker { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Marker Offset", Description = "Offset in ticks for markers", Order = 15, GroupName = "4. Visual")]
        public int MarkerOffset { get; set; }

        // Public property for strategy access
        [Browsable(false)]
        public bool IsUptrend
        {
            get { return isUptrend; }
        }
        
        [Browsable(false)]
        public double UpperBand
        {
            get { return upperBand; }
        }
        
        [Browsable(false)]
        public double LowerBand
        {
            get { return lowerBand; }
        }
        
        [Browsable(false)]
        public double ATR
        {
            get { return atrValue; }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EasyTrendEquivalent[] cacheEasyTrendEquivalent;
		public EasyTrendEquivalent EasyTrendEquivalent(EasyTrendMAType mAType, int period, bool smoothingEnabled, EasyTrendMAType smoothingMethod, int smoothingPeriod, bool filterEnabled, bool filterAfterSmoothing, double filterMultiplier, EasyTrendFilterUnit filterUnit, int filterATRPeriod, bool showPlot, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return EasyTrendEquivalent(Input, mAType, period, smoothingEnabled, smoothingMethod, smoothingPeriod, filterEnabled, filterAfterSmoothing, filterMultiplier, filterUnit, filterATRPeriod, showPlot, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}

		public EasyTrendEquivalent EasyTrendEquivalent(ISeries<double> input, EasyTrendMAType mAType, int period, bool smoothingEnabled, EasyTrendMAType smoothingMethod, int smoothingPeriod, bool filterEnabled, bool filterAfterSmoothing, double filterMultiplier, EasyTrendFilterUnit filterUnit, int filterATRPeriod, bool showPlot, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			if (cacheEasyTrendEquivalent != null)
				for (int idx = 0; idx < cacheEasyTrendEquivalent.Length; idx++)
					if (cacheEasyTrendEquivalent[idx] != null && cacheEasyTrendEquivalent[idx].MAType == mAType && cacheEasyTrendEquivalent[idx].Period == period && cacheEasyTrendEquivalent[idx].SmoothingEnabled == smoothingEnabled && cacheEasyTrendEquivalent[idx].SmoothingMethod == smoothingMethod && cacheEasyTrendEquivalent[idx].SmoothingPeriod == smoothingPeriod && cacheEasyTrendEquivalent[idx].FilterEnabled == filterEnabled && cacheEasyTrendEquivalent[idx].FilterAfterSmoothing == filterAfterSmoothing && cacheEasyTrendEquivalent[idx].FilterMultiplier == filterMultiplier && cacheEasyTrendEquivalent[idx].FilterUnit == filterUnit && cacheEasyTrendEquivalent[idx].FilterATRPeriod == filterATRPeriod && cacheEasyTrendEquivalent[idx].ShowPlot == showPlot && cacheEasyTrendEquivalent[idx].ShowMarkers == showMarkers && cacheEasyTrendEquivalent[idx].UptrendMarker == uptrendMarker && cacheEasyTrendEquivalent[idx].DowntrendMarker == downtrendMarker && cacheEasyTrendEquivalent[idx].MarkerOffset == markerOffset && cacheEasyTrendEquivalent[idx].EqualsInput(input))
						return cacheEasyTrendEquivalent[idx];
			return CacheIndicator<EasyTrendEquivalent>(new EasyTrendEquivalent(){ MAType = mAType, Period = period, SmoothingEnabled = smoothingEnabled, SmoothingMethod = smoothingMethod, SmoothingPeriod = smoothingPeriod, FilterEnabled = filterEnabled, FilterAfterSmoothing = filterAfterSmoothing, FilterMultiplier = filterMultiplier, FilterUnit = filterUnit, FilterATRPeriod = filterATRPeriod, ShowPlot = showPlot, ShowMarkers = showMarkers, UptrendMarker = uptrendMarker, DowntrendMarker = downtrendMarker, MarkerOffset = markerOffset }, input, ref cacheEasyTrendEquivalent);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EasyTrendEquivalent EasyTrendEquivalent(EasyTrendMAType mAType, int period, bool smoothingEnabled, EasyTrendMAType smoothingMethod, int smoothingPeriod, bool filterEnabled, bool filterAfterSmoothing, double filterMultiplier, EasyTrendFilterUnit filterUnit, int filterATRPeriod, bool showPlot, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.EasyTrendEquivalent(Input, mAType, period, smoothingEnabled, smoothingMethod, smoothingPeriod, filterEnabled, filterAfterSmoothing, filterMultiplier, filterUnit, filterATRPeriod, showPlot, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}

		public Indicators.EasyTrendEquivalent EasyTrendEquivalent(ISeries<double> input , EasyTrendMAType mAType, int period, bool smoothingEnabled, EasyTrendMAType smoothingMethod, int smoothingPeriod, bool filterEnabled, bool filterAfterSmoothing, double filterMultiplier, EasyTrendFilterUnit filterUnit, int filterATRPeriod, bool showPlot, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.EasyTrendEquivalent(input, mAType, period, smoothingEnabled, smoothingMethod, smoothingPeriod, filterEnabled, filterAfterSmoothing, filterMultiplier, filterUnit, filterATRPeriod, showPlot, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EasyTrendEquivalent EasyTrendEquivalent(EasyTrendMAType mAType, int period, bool smoothingEnabled, EasyTrendMAType smoothingMethod, int smoothingPeriod, bool filterEnabled, bool filterAfterSmoothing, double filterMultiplier, EasyTrendFilterUnit filterUnit, int filterATRPeriod, bool showPlot, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.EasyTrendEquivalent(Input, mAType, period, smoothingEnabled, smoothingMethod, smoothingPeriod, filterEnabled, filterAfterSmoothing, filterMultiplier, filterUnit, filterATRPeriod, showPlot, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}

		public Indicators.EasyTrendEquivalent EasyTrendEquivalent(ISeries<double> input , EasyTrendMAType mAType, int period, bool smoothingEnabled, EasyTrendMAType smoothingMethod, int smoothingPeriod, bool filterEnabled, bool filterAfterSmoothing, double filterMultiplier, EasyTrendFilterUnit filterUnit, int filterATRPeriod, bool showPlot, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.EasyTrendEquivalent(input, mAType, period, smoothingEnabled, smoothingMethod, smoothingPeriod, filterEnabled, filterAfterSmoothing, filterMultiplier, filterUnit, filterATRPeriod, showPlot, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}
	}
}

#endregion
