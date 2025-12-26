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
    public class SolarWaveEquivalent : Indicator
    {
        #region Private Variables
        private bool isUptrend;
        private int countWave;
        private double trendLine;
        private double trailingStop;
        private double referencePrice;
        
        // For ATR calculation
        private double[] trueRanges;
        private int trIndex;
        private double atrValue;
        
        // Reference price buffer
        private double[] refPriceBuffer;
        private int refIndex;
        
        // Slowdown detection
        private double[] momentumBuffer;
        private int momIndex;
        
        private bool isInitialized;
        private bool wasUptrend;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Solar Wave Equivalent - Trend and wave counting indicator";
                Name = "SolarWaveEquivalent";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                
                // Default parameters matching ninZaSolarWave
                OffsetATRPeriod = 100;
                OffsetMultiplierTrend = 2;
                OffsetMultiplierStop = 4;
                ReferencePricePeriod = 2;
                ReferencePriceCloseWeight = 1;
                SlowdownScan = 5;
                WeakWeakSplit = 10;
                PullbackSplit = 10;
                
                // Visual settings
                ShowTrailingStop = true;
                ShowMarkers = true;
                UptrendMarker = "▲ + Trend";
                DowntrendMarker = "Trend + ▼";
                MarkerOffset = 12;
                
                AddPlot(new Stroke(Brushes.DodgerBlue, DashStyleHelper.Dot, 2), PlotStyle.Line, "TrailingStop");
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                // Initialize arrays
                trueRanges = new double[OffsetATRPeriod];
                refPriceBuffer = new double[ReferencePricePeriod + 1];
                momentumBuffer = new double[SlowdownScan + 1];
                
                trIndex = 0;
                refIndex = 0;
                momIndex = 0;
                isInitialized = false;
                isUptrend = false;
                wasUptrend = false;
                countWave = 0;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
            {
                referencePrice = Close[0];
                trendLine = Close[0];
                trailingStop = Close[0];
                Values[0][0] = trailingStop;
                return;
            }

            // Calculate True Range for ATR
            double trueRange = Math.Max(High[0] - Low[0], 
                              Math.Max(Math.Abs(High[0] - Close[1]), Math.Abs(Low[0] - Close[1])));
            
            // Update TR circular buffer
            trueRanges[trIndex] = trueRange;
            trIndex = (trIndex + 1) % OffsetATRPeriod;
            
            // Calculate ATR
            if (CurrentBar >= OffsetATRPeriod)
            {
                atrValue = 0;
                for (int i = 0; i < OffsetATRPeriod; i++)
                    atrValue += trueRanges[i];
                atrValue /= OffsetATRPeriod;
            }
            else
            {
                double sum = 0;
                int count = Math.Min(CurrentBar + 1, OffsetATRPeriod);
                for (int i = 0; i < count; i++)
                    sum += trueRanges[i];
                atrValue = sum / count;
            }

            // Calculate reference price (weighted average of recent prices)
            // ReferencePriceCloseWeight determines how much weight Close gets vs HL average
            double hlAvg = (High[0] + Low[0]) / 2;
            double weightedPrice = (Close[0] * ReferencePriceCloseWeight + hlAvg) / (ReferencePriceCloseWeight + 1);
            
            // Store in reference price buffer
            refPriceBuffer[refIndex] = weightedPrice;
            refIndex = (refIndex + 1) % refPriceBuffer.Length;
            
            // Calculate smoothed reference price
            double refPriceSum = 0;
            int refCount = Math.Min(CurrentBar + 1, ReferencePricePeriod);
            for (int i = 0; i < refCount; i++)
                refPriceSum += refPriceBuffer[i];
            referencePrice = refPriceSum / refCount;

            // Calculate trend and stop offsets
            double trendOffset = OffsetMultiplierTrend * atrValue;
            double stopOffset = OffsetMultiplierStop * atrValue;

            // Store previous state
            wasUptrend = isUptrend;
            int previousWaveCount = countWave;

            // Determine trend based on price vs trailing stop
            if (!isInitialized && CurrentBar >= OffsetATRPeriod)
            {
                // Initialize trend
                isUptrend = Close[0] > referencePrice;
                trailingStop = isUptrend ? referencePrice - stopOffset : referencePrice + stopOffset;
                countWave = 0;
                isInitialized = true;
            }
            else if (isInitialized)
            {
                if (isUptrend)
                {
                    // In uptrend - trailing stop moves up, never down
                    double newStop = referencePrice - stopOffset;
                    if (newStop > trailingStop)
                        trailingStop = newStop;
                    
                    // Trend reverses if price closes below trailing stop
                    if (Close[0] < trailingStop)
                    {
                        isUptrend = false;
                        trailingStop = referencePrice + stopOffset;
                        countWave = -1; // Start counting down
                    }
                    else
                    {
                        // Continue uptrend - increment wave count
                        if (Close[0] > Close[1])
                            countWave = Math.Max(1, countWave + 1);
                        else if (Close[0] < Close[1])
                            countWave = Math.Max(1, countWave - 1); // Pullback reduces count but stays positive
                    }
                }
                else
                {
                    // In downtrend - trailing stop moves down, never up
                    double newStop = referencePrice + stopOffset;
                    if (newStop < trailingStop)
                        trailingStop = newStop;
                    
                    // Trend reverses if price closes above trailing stop
                    if (Close[0] > trailingStop)
                    {
                        isUptrend = true;
                        trailingStop = referencePrice - stopOffset;
                        countWave = 1; // Start counting up
                    }
                    else
                    {
                        // Continue downtrend - decrement wave count (more negative)
                        if (Close[0] < Close[1])
                            countWave = Math.Min(-1, countWave - 1);
                        else if (Close[0] > Close[1])
                            countWave = Math.Min(-1, countWave + 1); // Pullback reduces magnitude but stays negative
                    }
                }
            }

            // Calculate trend line (for reference)
            trendLine = isUptrend ? referencePrice - trendOffset : referencePrice + trendOffset;

            // Set plot value
            Values[0][0] = trailingStop;
            
            // Update plot color based on trend
            if (ShowTrailingStop)
            {
                PlotBrushes[0][0] = isUptrend ? Brushes.DodgerBlue : Brushes.HotPink;
            }
            
            // Draw markers on trend change
            if (ShowMarkers && isInitialized && isUptrend != wasUptrend)
            {
                if (isUptrend)
                {
                    Draw.Text(this, "Up" + CurrentBar, UptrendMarker, 0, Low[0] - MarkerOffset * TickSize, Brushes.LimeGreen);
                }
                else
                {
                    Draw.Text(this, "Down" + CurrentBar, DowntrendMarker, 0, High[0] + MarkerOffset * TickSize, Brushes.OrangeRed);
                }
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", Description = "Period for ATR calculation", Order = 1, GroupName = "1. Offset Settings")]
        public int OffsetATRPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "Trend Multiplier", Description = "ATR multiplier for trend offset", Order = 2, GroupName = "1. Offset Settings")]
        public double OffsetMultiplierTrend { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "Stop Multiplier", Description = "ATR multiplier for trailing stop", Order = 3, GroupName = "1. Offset Settings")]
        public double OffsetMultiplierStop { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Reference Price Period", Description = "Period for reference price smoothing", Order = 4, GroupName = "2. Reference Price")]
        public int ReferencePricePeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Close Weight", Description = "Weight for Close vs HL average", Order = 5, GroupName = "2. Reference Price")]
        public int ReferencePriceCloseWeight { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Slowdown Scan", Description = "Bars to scan for slowdown", Order = 6, GroupName = "3. Wave Detection")]
        public int SlowdownScan { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Weak/Weak Split", Description = "Threshold for weak trend", Order = 7, GroupName = "3. Wave Detection")]
        public int WeakWeakSplit { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Pullback Split", Description = "Threshold for pullback detection", Order = 8, GroupName = "3. Wave Detection")]
        public int PullbackSplit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Trailing Stop", Description = "Show trailing stop line", Order = 9, GroupName = "4. Visual")]
        public bool ShowTrailingStop { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Markers", Description = "Show trend change markers", Order = 10, GroupName = "4. Visual")]
        public bool ShowMarkers { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Uptrend Marker", Description = "Symbol for uptrend start", Order = 11, GroupName = "4. Visual")]
        public string UptrendMarker { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Downtrend Marker", Description = "Symbol for downtrend start", Order = 12, GroupName = "4. Visual")]
        public string DowntrendMarker { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Marker Offset", Description = "Offset in ticks for markers", Order = 13, GroupName = "4. Visual")]
        public int MarkerOffset { get; set; }

        // Public properties for strategy access
        [Browsable(false)]
        public bool IsUptrend
        {
            get { return isUptrend; }
        }
        
        [Browsable(false)]
        public int CountWave
        {
            get { return countWave; }
        }
        
        [Browsable(false)]
        public double TrailingStop
        {
            get { return trailingStop; }
        }
        
        [Browsable(false)]
        public double TrendLine
        {
            get { return trendLine; }
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
		private SolarWaveEquivalent[] cacheSolarWaveEquivalent;
		public SolarWaveEquivalent SolarWaveEquivalent(int offsetATRPeriod, double offsetMultiplierTrend, double offsetMultiplierStop, int referencePricePeriod, int referencePriceCloseWeight, int slowdownScan, int weakWeakSplit, int pullbackSplit, bool showTrailingStop, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return SolarWaveEquivalent(Input, offsetATRPeriod, offsetMultiplierTrend, offsetMultiplierStop, referencePricePeriod, referencePriceCloseWeight, slowdownScan, weakWeakSplit, pullbackSplit, showTrailingStop, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}

		public SolarWaveEquivalent SolarWaveEquivalent(ISeries<double> input, int offsetATRPeriod, double offsetMultiplierTrend, double offsetMultiplierStop, int referencePricePeriod, int referencePriceCloseWeight, int slowdownScan, int weakWeakSplit, int pullbackSplit, bool showTrailingStop, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			if (cacheSolarWaveEquivalent != null)
				for (int idx = 0; idx < cacheSolarWaveEquivalent.Length; idx++)
					if (cacheSolarWaveEquivalent[idx] != null && cacheSolarWaveEquivalent[idx].OffsetATRPeriod == offsetATRPeriod && cacheSolarWaveEquivalent[idx].OffsetMultiplierTrend == offsetMultiplierTrend && cacheSolarWaveEquivalent[idx].OffsetMultiplierStop == offsetMultiplierStop && cacheSolarWaveEquivalent[idx].ReferencePricePeriod == referencePricePeriod && cacheSolarWaveEquivalent[idx].ReferencePriceCloseWeight == referencePriceCloseWeight && cacheSolarWaveEquivalent[idx].SlowdownScan == slowdownScan && cacheSolarWaveEquivalent[idx].WeakWeakSplit == weakWeakSplit && cacheSolarWaveEquivalent[idx].PullbackSplit == pullbackSplit && cacheSolarWaveEquivalent[idx].ShowTrailingStop == showTrailingStop && cacheSolarWaveEquivalent[idx].ShowMarkers == showMarkers && cacheSolarWaveEquivalent[idx].UptrendMarker == uptrendMarker && cacheSolarWaveEquivalent[idx].DowntrendMarker == downtrendMarker && cacheSolarWaveEquivalent[idx].MarkerOffset == markerOffset && cacheSolarWaveEquivalent[idx].EqualsInput(input))
						return cacheSolarWaveEquivalent[idx];
			return CacheIndicator<SolarWaveEquivalent>(new SolarWaveEquivalent(){ OffsetATRPeriod = offsetATRPeriod, OffsetMultiplierTrend = offsetMultiplierTrend, OffsetMultiplierStop = offsetMultiplierStop, ReferencePricePeriod = referencePricePeriod, ReferencePriceCloseWeight = referencePriceCloseWeight, SlowdownScan = slowdownScan, WeakWeakSplit = weakWeakSplit, PullbackSplit = pullbackSplit, ShowTrailingStop = showTrailingStop, ShowMarkers = showMarkers, UptrendMarker = uptrendMarker, DowntrendMarker = downtrendMarker, MarkerOffset = markerOffset }, input, ref cacheSolarWaveEquivalent);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.SolarWaveEquivalent SolarWaveEquivalent(int offsetATRPeriod, double offsetMultiplierTrend, double offsetMultiplierStop, int referencePricePeriod, int referencePriceCloseWeight, int slowdownScan, int weakWeakSplit, int pullbackSplit, bool showTrailingStop, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.SolarWaveEquivalent(Input, offsetATRPeriod, offsetMultiplierTrend, offsetMultiplierStop, referencePricePeriod, referencePriceCloseWeight, slowdownScan, weakWeakSplit, pullbackSplit, showTrailingStop, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}

		public Indicators.SolarWaveEquivalent SolarWaveEquivalent(ISeries<double> input , int offsetATRPeriod, double offsetMultiplierTrend, double offsetMultiplierStop, int referencePricePeriod, int referencePriceCloseWeight, int slowdownScan, int weakWeakSplit, int pullbackSplit, bool showTrailingStop, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.SolarWaveEquivalent(input, offsetATRPeriod, offsetMultiplierTrend, offsetMultiplierStop, referencePricePeriod, referencePriceCloseWeight, slowdownScan, weakWeakSplit, pullbackSplit, showTrailingStop, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.SolarWaveEquivalent SolarWaveEquivalent(int offsetATRPeriod, double offsetMultiplierTrend, double offsetMultiplierStop, int referencePricePeriod, int referencePriceCloseWeight, int slowdownScan, int weakWeakSplit, int pullbackSplit, bool showTrailingStop, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.SolarWaveEquivalent(Input, offsetATRPeriod, offsetMultiplierTrend, offsetMultiplierStop, referencePricePeriod, referencePriceCloseWeight, slowdownScan, weakWeakSplit, pullbackSplit, showTrailingStop, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}

		public Indicators.SolarWaveEquivalent SolarWaveEquivalent(ISeries<double> input , int offsetATRPeriod, double offsetMultiplierTrend, double offsetMultiplierStop, int referencePricePeriod, int referencePriceCloseWeight, int slowdownScan, int weakWeakSplit, int pullbackSplit, bool showTrailingStop, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.SolarWaveEquivalent(input, offsetATRPeriod, offsetMultiplierTrend, offsetMultiplierStop, referencePricePeriod, referencePriceCloseWeight, slowdownScan, weakWeakSplit, pullbackSplit, showTrailingStop, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}
	}
}

#endregion
