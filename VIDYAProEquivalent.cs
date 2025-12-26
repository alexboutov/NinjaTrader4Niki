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

namespace NinjaTrader.NinjaScript.Indicators
{
    public enum VIDYAProMAType
    {
        SMA,
        EMA,
        WMA,
        DEMA,
        TEMA,
        None
    }

    public class VIDYAProEquivalent : Indicator
    {
        #region Private Variables
        private double vidyaValue;
        private double smoothedValue;
        private double previousSmoothedValue;
        private bool isUptrend;
        private double upperBand;
        private double lowerBand;
        
        // For CMO calculation
        private double[] priceChanges;
        private int changeIndex;
        private double sumUp;
        private double sumDown;
        
        // For smoothing
        private double[] smoothingBuffer;
        private int smoothingIndex;
        
        // For ATR
        private double[] trueRanges;
        private int trIndex;
        private double atrValue;
        
        private bool isInitialized;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"VIDYA Pro Equivalent - Variable Index Dynamic Average with trend detection";
                Name = "VIDYAProEquivalent";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                
                // Default parameters matching ninZaVIDYAPro
                Period = 9;
                VolatilityPeriod = 9;
                SmoothingEnabled = true;
                SmoothingMethod = VIDYAProMAType.EMA;
                SmoothingPeriod = 5;
                FilterEnabled = true;
                FilterMultiplier = 4.0;
                ATRPeriod = 14;
                
                // Visual settings
                ShowPlot = true;
                ShowMarkers = true;
                UptrendMarker = "▲";
                DowntrendMarker = "▼";
                MarkerOffset = 10;
                
                AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "VIDYA");
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                // Initialize arrays
                priceChanges = new double[VolatilityPeriod];
                smoothingBuffer = new double[SmoothingPeriod];
                trueRanges = new double[ATRPeriod];
                
                changeIndex = 0;
                smoothingIndex = 0;
                trIndex = 0;
                sumUp = 0;
                sumDown = 0;
                isInitialized = false;
                isUptrend = false;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
            {
                vidyaValue = Close[0];
                smoothedValue = Close[0];
                previousSmoothedValue = Close[0];
                Value[0] = Close[0];
                return;
            }

            // Calculate True Range for ATR
            double trueRange = Math.Max(High[0] - Low[0], 
                              Math.Max(Math.Abs(High[0] - Close[1]), Math.Abs(Low[0] - Close[1])));
            
            // Update TR circular buffer
            trueRanges[trIndex] = trueRange;
            trIndex = (trIndex + 1) % ATRPeriod;
            
            // Calculate ATR
            if (CurrentBar >= ATRPeriod)
            {
                atrValue = 0;
                for (int i = 0; i < ATRPeriod; i++)
                    atrValue += trueRanges[i];
                atrValue /= ATRPeriod;
            }
            else
            {
                atrValue = trueRange;
            }

            // Calculate price change for CMO
            double priceChange = Close[0] - Close[1];
            
            // Remove old value from sums if buffer is full
            if (CurrentBar >= VolatilityPeriod)
            {
                double oldChange = priceChanges[changeIndex];
                if (oldChange > 0)
                    sumUp -= oldChange;
                else
                    sumDown -= Math.Abs(oldChange);
            }
            
            // Add new value to sums
            if (priceChange > 0)
                sumUp += priceChange;
            else
                sumDown += Math.Abs(priceChange);
            
            // Store in circular buffer
            priceChanges[changeIndex] = priceChange;
            changeIndex = (changeIndex + 1) % VolatilityPeriod;
            
            // Calculate CMO (Chande Momentum Oscillator)
            double cmo = 0;
            if (sumUp + sumDown != 0)
                cmo = (sumUp - sumDown) / (sumUp + sumDown);
            
            // Calculate VIDYA
            // Alpha is the standard EMA smoothing constant
            double alpha = 2.0 / (Period + 1);
            // VIDYA uses absolute CMO to scale the smoothing
            double scaledAlpha = alpha * Math.Abs(cmo);
            
            // VIDYA formula
            vidyaValue = scaledAlpha * Close[0] + (1 - scaledAlpha) * vidyaValue;
            
            // Apply smoothing if enabled
            double outputValue;
            if (SmoothingEnabled && SmoothingMethod != VIDYAProMAType.None)
            {
                outputValue = ApplySmoothing(vidyaValue);
            }
            else
            {
                outputValue = vidyaValue;
            }
            
            // Store previous for trend detection
            previousSmoothedValue = smoothedValue;
            smoothedValue = outputValue;
            
            // Set plot value
            Value[0] = outputValue;
            
            // Calculate filter bands
            double filterDistance = FilterEnabled ? FilterMultiplier * atrValue : 0;
            upperBand = outputValue + filterDistance;
            lowerBand = outputValue - filterDistance;
            
            // Determine trend with filter
            bool previousTrend = isUptrend;
            
            if (CurrentBar >= Math.Max(Period, VolatilityPeriod) + 1)
            {
                if (!isInitialized)
                {
                    // Initialize trend based on price vs VIDYA
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
                    // Without filter: simple price vs VIDYA comparison
                    isUptrend = Close[0] > outputValue;
                }
            }
            
            // Update plot color based on trend
            if (ShowPlot)
            {
                PlotBrushes[0][0] = isUptrend ? Brushes.DodgerBlue : Brushes.HotPink;
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
        
        private double ApplySmoothing(double value)
        {
            // Store value in circular buffer
            smoothingBuffer[smoothingIndex] = value;
            smoothingIndex = (smoothingIndex + 1) % SmoothingPeriod;
            
            if (CurrentBar < SmoothingPeriod)
            {
                // Not enough data, return simple average of available values
                double sum = 0;
                int count = Math.Min(CurrentBar + 1, SmoothingPeriod);
                for (int i = 0; i < count; i++)
                    sum += smoothingBuffer[i];
                return sum / count;
            }
            
            switch (SmoothingMethod)
            {
                case VIDYAProMAType.SMA:
                    return CalculateSMA();
                    
                case VIDYAProMAType.EMA:
                    return CalculateEMA(value);
                    
                case VIDYAProMAType.WMA:
                    return CalculateWMA();
                    
                case VIDYAProMAType.DEMA:
                    return CalculateDEMA(value);
                    
                case VIDYAProMAType.TEMA:
                    return CalculateTEMA(value);
                    
                default:
                    return value;
            }
        }
        
        private double CalculateSMA()
        {
            double sum = 0;
            for (int i = 0; i < SmoothingPeriod; i++)
                sum += smoothingBuffer[i];
            return sum / SmoothingPeriod;
        }
        
        private double emaValue = 0;
        private double CalculateEMA(double value)
        {
            double k = 2.0 / (SmoothingPeriod + 1);
            emaValue = (value - emaValue) * k + emaValue;
            return emaValue;
        }
        
        private double CalculateWMA()
        {
            double sum = 0;
            double weightSum = 0;
            int weight = 1;
            
            // Get values in chronological order from circular buffer
            for (int i = 0; i < SmoothingPeriod; i++)
            {
                int idx = (smoothingIndex + i) % SmoothingPeriod;
                sum += smoothingBuffer[idx] * weight;
                weightSum += weight;
                weight++;
            }
            return sum / weightSum;
        }
        
        private double ema1 = 0, ema2 = 0;
        private double CalculateDEMA(double value)
        {
            double k = 2.0 / (SmoothingPeriod + 1);
            ema1 = (value - ema1) * k + ema1;
            ema2 = (ema1 - ema2) * k + ema2;
            return 2 * ema1 - ema2;
        }
        
        private double ema1T = 0, ema2T = 0, ema3T = 0;
        private double CalculateTEMA(double value)
        {
            double k = 2.0 / (SmoothingPeriod + 1);
            ema1T = (value - ema1T) * k + ema1T;
            ema2T = (ema1T - ema2T) * k + ema2T;
            ema3T = (ema2T - ema3T) * k + ema3T;
            return 3 * ema1T - 3 * ema2T + ema3T;
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Period", Description = "VIDYA smoothing period", Order = 1, GroupName = "1. VIDYA Parameters")]
        public int Period { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Volatility Period", Description = "Period for CMO volatility calculation", Order = 2, GroupName = "1. VIDYA Parameters")]
        public int VolatilityPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Smoothing Enabled", Description = "Enable additional smoothing", Order = 3, GroupName = "2. Smoothing")]
        public bool SmoothingEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Smoothing Method", Description = "Type of smoothing to apply", Order = 4, GroupName = "2. Smoothing")]
        public VIDYAProMAType SmoothingMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Smoothing Period", Description = "Period for smoothing", Order = 5, GroupName = "2. Smoothing")]
        public int SmoothingPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filter Enabled", Description = "Enable ATR-based trend filter", Order = 6, GroupName = "3. Filter")]
        public bool FilterEnabled { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "Filter Multiplier", Description = "ATR multiplier for trend filter", Order = 7, GroupName = "3. Filter")]
        public double FilterMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", Description = "Period for ATR calculation", Order = 8, GroupName = "3. Filter")]
        public int ATRPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Plot", Description = "Show VIDYA line on chart", Order = 9, GroupName = "4. Visual")]
        public bool ShowPlot { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Markers", Description = "Show trend change markers", Order = 10, GroupName = "4. Visual")]
        public bool ShowMarkers { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Uptrend Marker", Description = "Symbol for uptrend", Order = 11, GroupName = "4. Visual")]
        public string UptrendMarker { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Downtrend Marker", Description = "Symbol for downtrend", Order = 12, GroupName = "4. Visual")]
        public string DowntrendMarker { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Marker Offset", Description = "Offset in ticks for markers", Order = 13, GroupName = "4. Visual")]
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
		private VIDYAProEquivalent[] cacheVIDYAProEquivalent;
		public VIDYAProEquivalent VIDYAProEquivalent(int period, int volatilityPeriod, bool smoothingEnabled, NinjaTrader.NinjaScript.Indicators.VIDYAProMAType smoothingMethod, int smoothingPeriod, bool filterEnabled, double filterMultiplier, int aTRPeriod, bool showPlot, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return VIDYAProEquivalent(Input, period, volatilityPeriod, smoothingEnabled, smoothingMethod, smoothingPeriod, filterEnabled, filterMultiplier, aTRPeriod, showPlot, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}

		public VIDYAProEquivalent VIDYAProEquivalent(ISeries<double> input, int period, int volatilityPeriod, bool smoothingEnabled, NinjaTrader.NinjaScript.Indicators.VIDYAProMAType smoothingMethod, int smoothingPeriod, bool filterEnabled, double filterMultiplier, int aTRPeriod, bool showPlot, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			if (cacheVIDYAProEquivalent != null)
				for (int idx = 0; idx < cacheVIDYAProEquivalent.Length; idx++)
					if (cacheVIDYAProEquivalent[idx] != null && cacheVIDYAProEquivalent[idx].Period == period && cacheVIDYAProEquivalent[idx].VolatilityPeriod == volatilityPeriod && cacheVIDYAProEquivalent[idx].SmoothingEnabled == smoothingEnabled && cacheVIDYAProEquivalent[idx].SmoothingMethod == smoothingMethod && cacheVIDYAProEquivalent[idx].SmoothingPeriod == smoothingPeriod && cacheVIDYAProEquivalent[idx].FilterEnabled == filterEnabled && cacheVIDYAProEquivalent[idx].FilterMultiplier == filterMultiplier && cacheVIDYAProEquivalent[idx].ATRPeriod == aTRPeriod && cacheVIDYAProEquivalent[idx].ShowPlot == showPlot && cacheVIDYAProEquivalent[idx].ShowMarkers == showMarkers && cacheVIDYAProEquivalent[idx].UptrendMarker == uptrendMarker && cacheVIDYAProEquivalent[idx].DowntrendMarker == downtrendMarker && cacheVIDYAProEquivalent[idx].MarkerOffset == markerOffset && cacheVIDYAProEquivalent[idx].EqualsInput(input))
						return cacheVIDYAProEquivalent[idx];
			return CacheIndicator<VIDYAProEquivalent>(new VIDYAProEquivalent(){ Period = period, VolatilityPeriod = volatilityPeriod, SmoothingEnabled = smoothingEnabled, SmoothingMethod = smoothingMethod, SmoothingPeriod = smoothingPeriod, FilterEnabled = filterEnabled, FilterMultiplier = filterMultiplier, ATRPeriod = aTRPeriod, ShowPlot = showPlot, ShowMarkers = showMarkers, UptrendMarker = uptrendMarker, DowntrendMarker = downtrendMarker, MarkerOffset = markerOffset }, input, ref cacheVIDYAProEquivalent);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.VIDYAProEquivalent VIDYAProEquivalent(int period, int volatilityPeriod, bool smoothingEnabled, NinjaTrader.NinjaScript.Indicators.VIDYAProMAType smoothingMethod, int smoothingPeriod, bool filterEnabled, double filterMultiplier, int aTRPeriod, bool showPlot, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.VIDYAProEquivalent(Input, period, volatilityPeriod, smoothingEnabled, smoothingMethod, smoothingPeriod, filterEnabled, filterMultiplier, aTRPeriod, showPlot, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}

		public Indicators.VIDYAProEquivalent VIDYAProEquivalent(ISeries<double> input, int period, int volatilityPeriod, bool smoothingEnabled, NinjaTrader.NinjaScript.Indicators.VIDYAProMAType smoothingMethod, int smoothingPeriod, bool filterEnabled, double filterMultiplier, int aTRPeriod, bool showPlot, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.VIDYAProEquivalent(input, period, volatilityPeriod, smoothingEnabled, smoothingMethod, smoothingPeriod, filterEnabled, filterMultiplier, aTRPeriod, showPlot, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}
	}
}

#endregion
