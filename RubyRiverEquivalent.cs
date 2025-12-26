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

public enum RubyRiverMAType
{
    SMA,
    EMA,
    WMA,
    DEMA,
    TEMA,
    LinReg
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class RubyRiverEquivalent : Indicator
    {
        #region Private Variables
        private double maValue;
        private double smoothedMA;
        private double highMA;
        private double lowMA;
        private double offset;
        private bool isUptrend;
        private bool isPullback;
        
        // For MA calculation
        private double emaState;
        private double ema1State, ema2State;
        private double ema1TState, ema2TState, ema3TState;
        
        // For smoothing (LinReg or other)
        private double smoothEmaState;
        private double smoothEma1State, smoothEma2State;
        private double smoothEma1TState, smoothEma2TState, smoothEma3TState;
        
        // For offset calculation (ATR-based)
        private double[] trueRanges;
        private int trIndex;
        private double atrValue;
        
        // Circular buffers for SMA/WMA/LinReg
        private double[] maBuffer;
        private double[] smoothBuffer;
        private int maIndex;
        private int smoothIndex;
        
        private bool isInitialized;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Ruby River Equivalent - Channel-based trend indicator";
                Name = "RubyRiverEquivalent";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                
                // Default parameters matching ninZaRubyRiver
                MAType = RubyRiverMAType.EMA;
                MAPeriod = 20;
                MASmoothingEnabled = true;
                MASmoothingMethod = RubyRiverMAType.LinReg;
                MASmoothingPeriod = 5;
                OffsetMultiplier = 0.15;
                OffsetPeriod = 100;
                
                // Visual settings
                ShowPlot = true;
                ShowMarkers = true;
                UptrendMarker = "▲";
                DowntrendMarker = "▼";
                MarkerOffset = 10;
                
                AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "HighMA");
                AddPlot(new Stroke(Brushes.Crimson, 2), PlotStyle.Line, "LowMA");
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                // Initialize arrays
                maBuffer = new double[Math.Max(MAPeriod, MASmoothingPeriod) + 1];
                smoothBuffer = new double[MASmoothingPeriod + 1];
                trueRanges = new double[OffsetPeriod];
                
                maIndex = 0;
                smoothIndex = 0;
                trIndex = 0;
                isInitialized = false;
                isUptrend = false;
                isPullback = false;
                
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
                double midPrice = (High[0] + Low[0]) / 2;
                maValue = midPrice;
                smoothedMA = midPrice;
                highMA = High[0];
                lowMA = Low[0];
                Values[0][0] = highMA;
                Values[1][0] = lowMA;
                emaState = Close[0];
                smoothEmaState = Close[0];
                ema1State = ema2State = Close[0];
                smoothEma1State = smoothEma2State = Close[0];
                ema1TState = ema2TState = ema3TState = Close[0];
                smoothEma1TState = smoothEma2TState = smoothEma3TState = Close[0];
                return;
            }

            // Calculate True Range for ATR-based offset
            double trueRange = Math.Max(High[0] - Low[0], 
                              Math.Max(Math.Abs(High[0] - Close[1]), Math.Abs(Low[0] - Close[1])));
            
            // Update TR circular buffer
            trueRanges[trIndex] = trueRange;
            trIndex = (trIndex + 1) % OffsetPeriod;
            
            // Calculate ATR for offset
            if (CurrentBar >= OffsetPeriod)
            {
                atrValue = 0;
                for (int i = 0; i < OffsetPeriod; i++)
                    atrValue += trueRanges[i];
                atrValue /= OffsetPeriod;
            }
            else
            {
                double sum = 0;
                int count = Math.Min(CurrentBar + 1, OffsetPeriod);
                for (int i = 0; i < count; i++)
                    sum += trueRanges[i];
                atrValue = sum / count;
            }
            
            // Calculate offset
            offset = OffsetMultiplier * atrValue;

            // Calculate base MA on Close price
            maValue = CalculateMA(Close[0], MAType, MAPeriod, ref emaState, ref ema1State, ref ema2State, 
                                  ref ema1TState, ref ema2TState, ref ema3TState, maBuffer, ref maIndex);
            
            // Store in MA buffer
            maBuffer[maIndex] = Close[0];
            maIndex = (maIndex + 1) % maBuffer.Length;
            
            // Apply smoothing if enabled
            if (MASmoothingEnabled)
            {
                smoothedMA = CalculateMA(maValue, MASmoothingMethod, MASmoothingPeriod, ref smoothEmaState, 
                                         ref smoothEma1State, ref smoothEma2State, ref smoothEma1TState, 
                                         ref smoothEma2TState, ref smoothEma3TState, smoothBuffer, ref smoothIndex);
                
                smoothBuffer[smoothIndex] = maValue;
                smoothIndex = (smoothIndex + 1) % smoothBuffer.Length;
            }
            else
            {
                smoothedMA = maValue;
            }
            
            // Calculate high and low MAs (the "river" channel)
            highMA = smoothedMA + offset;
            lowMA = smoothedMA - offset;
            
            // Set plot values
            Values[0][0] = highMA;
            Values[1][0] = lowMA;
            
            // Determine trend
            bool previousTrend = isUptrend;
            bool previousPullback = isPullback;
            
            if (CurrentBar >= MAPeriod + 1)
            {
                if (!isInitialized)
                {
                    // Initialize trend based on price position
                    isUptrend = Close[0] > highMA;
                    isInitialized = true;
                }
                else
                {
                    // Trend switches when price crosses the opposite band
                    if (isUptrend && Close[0] < lowMA)
                        isUptrend = false;
                    else if (!isUptrend && Close[0] > highMA)
                        isUptrend = true;
                }
                
                // Detect pullback (price inside the river)
                isPullback = (Close[0] <= highMA && Close[0] >= lowMA);
            }
            
            // Update plot colors based on trend
            if (ShowPlot)
            {
                Brush trendBrush = isUptrend ? Brushes.DodgerBlue : Brushes.Crimson;
                PlotBrushes[0][0] = trendBrush;
                PlotBrushes[1][0] = trendBrush;
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
                    Draw.Text(this, "Down" + CurrentBar, DowntrendMarker, 0, High[0] + MarkerOffset * TickSize, Brushes.Crimson);
                }
            }
        }
        
        private double CalculateMA(double value, RubyRiverMAType maType, int period, 
                                   ref double ema, ref double ema1, ref double ema2, 
                                   ref double ema1T, ref double ema2T, ref double ema3T,
                                   double[] buffer, ref int bufferIndex)
        {
            double k = 2.0 / (period + 1);
            
            switch (maType)
            {
                case RubyRiverMAType.SMA:
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
                    
                case RubyRiverMAType.EMA:
                    if (CurrentBar < period)
                    {
                        ema = (ema * CurrentBar + value) / (CurrentBar + 1);
                    }
                    else
                    {
                        ema = (value - ema) * k + ema;
                    }
                    return ema;
                    
                case RubyRiverMAType.WMA:
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
                    
                case RubyRiverMAType.DEMA:
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
                    
                case RubyRiverMAType.TEMA:
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
                    
                case RubyRiverMAType.LinReg:
                    return CalculateLinReg(value, period, buffer, bufferIndex);
                    
                default:
                    return value;
            }
        }
        
        private double CalculateLinReg(double currentValue, int period, double[] buffer, int bufferIndex)
        {
            int count = Math.Min(CurrentBar + 1, period);
            if (count < 2) return currentValue;
            
            // Linear regression calculation
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            
            for (int i = 0; i < count; i++)
            {
                double y = (i == count - 1) ? currentValue : buffer[(bufferIndex + i) % buffer.Length];
                double x = i;
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumX2 += x * x;
            }
            
            double n = count;
            double slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
            double intercept = (sumY - slope * sumX) / n;
            
            // Return the endpoint of the regression line
            return intercept + slope * (count - 1);
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "MA Type", Description = "Type of moving average", Order = 1, GroupName = "1. Moving Average")]
        public RubyRiverMAType MAType { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "MA Period", Description = "MA period", Order = 2, GroupName = "1. Moving Average")]
        public int MAPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Smoothing Enabled", Description = "Enable additional smoothing", Order = 3, GroupName = "2. Smoothing")]
        public bool MASmoothingEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Smoothing Method", Description = "Type of smoothing to apply", Order = 4, GroupName = "2. Smoothing")]
        public RubyRiverMAType MASmoothingMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Smoothing Period", Description = "Period for smoothing", Order = 5, GroupName = "2. Smoothing")]
        public int MASmoothingPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Offset Multiplier", Description = "ATR multiplier for channel offset", Order = 6, GroupName = "3. Channel")]
        public double OffsetMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Offset Period", Description = "Period for ATR calculation", Order = 7, GroupName = "3. Channel")]
        public int OffsetPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Plot", Description = "Show channel lines on chart", Order = 8, GroupName = "4. Visual")]
        public bool ShowPlot { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Markers", Description = "Show trend change markers", Order = 9, GroupName = "4. Visual")]
        public bool ShowMarkers { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Uptrend Marker", Description = "Symbol for uptrend", Order = 10, GroupName = "4. Visual")]
        public string UptrendMarker { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Downtrend Marker", Description = "Symbol for downtrend", Order = 11, GroupName = "4. Visual")]
        public string DowntrendMarker { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Marker Offset", Description = "Offset in ticks for markers", Order = 12, GroupName = "4. Visual")]
        public int MarkerOffset { get; set; }

        // Public properties for strategy access
        [Browsable(false)]
        public bool IsUptrend
        {
            get { return isUptrend; }
        }
        
        [Browsable(false)]
        public bool IsPullback
        {
            get { return isPullback; }
        }
        
        [Browsable(false)]
        public double HighMA
        {
            get { return highMA; }
        }
        
        [Browsable(false)]
        public double LowMA
        {
            get { return lowMA; }
        }
        
        [Browsable(false)]
        public double Offset
        {
            get { return offset; }
        }
        #endregion
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RubyRiverEquivalent[] cacheRubyRiverEquivalent;
		public RubyRiverEquivalent RubyRiverEquivalent(RubyRiverMAType mAType, int mAPeriod, bool mASmoothingEnabled, RubyRiverMAType mASmoothingMethod, int mASmoothingPeriod, double offsetMultiplier, int offsetPeriod, bool showPlot, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return RubyRiverEquivalent(Input, mAType, mAPeriod, mASmoothingEnabled, mASmoothingMethod, mASmoothingPeriod, offsetMultiplier, offsetPeriod, showPlot, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}

		public RubyRiverEquivalent RubyRiverEquivalent(ISeries<double> input, RubyRiverMAType mAType, int mAPeriod, bool mASmoothingEnabled, RubyRiverMAType mASmoothingMethod, int mASmoothingPeriod, double offsetMultiplier, int offsetPeriod, bool showPlot, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			if (cacheRubyRiverEquivalent != null)
				for (int idx = 0; idx < cacheRubyRiverEquivalent.Length; idx++)
					if (cacheRubyRiverEquivalent[idx] != null && cacheRubyRiverEquivalent[idx].MAType == mAType && cacheRubyRiverEquivalent[idx].MAPeriod == mAPeriod && cacheRubyRiverEquivalent[idx].MASmoothingEnabled == mASmoothingEnabled && cacheRubyRiverEquivalent[idx].MASmoothingMethod == mASmoothingMethod && cacheRubyRiverEquivalent[idx].MASmoothingPeriod == mASmoothingPeriod && cacheRubyRiverEquivalent[idx].OffsetMultiplier == offsetMultiplier && cacheRubyRiverEquivalent[idx].OffsetPeriod == offsetPeriod && cacheRubyRiverEquivalent[idx].ShowPlot == showPlot && cacheRubyRiverEquivalent[idx].ShowMarkers == showMarkers && cacheRubyRiverEquivalent[idx].UptrendMarker == uptrendMarker && cacheRubyRiverEquivalent[idx].DowntrendMarker == downtrendMarker && cacheRubyRiverEquivalent[idx].MarkerOffset == markerOffset && cacheRubyRiverEquivalent[idx].EqualsInput(input))
						return cacheRubyRiverEquivalent[idx];
			return CacheIndicator<RubyRiverEquivalent>(new RubyRiverEquivalent(){ MAType = mAType, MAPeriod = mAPeriod, MASmoothingEnabled = mASmoothingEnabled, MASmoothingMethod = mASmoothingMethod, MASmoothingPeriod = mASmoothingPeriod, OffsetMultiplier = offsetMultiplier, OffsetPeriod = offsetPeriod, ShowPlot = showPlot, ShowMarkers = showMarkers, UptrendMarker = uptrendMarker, DowntrendMarker = downtrendMarker, MarkerOffset = markerOffset }, input, ref cacheRubyRiverEquivalent);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RubyRiverEquivalent RubyRiverEquivalent(RubyRiverMAType mAType, int mAPeriod, bool mASmoothingEnabled, RubyRiverMAType mASmoothingMethod, int mASmoothingPeriod, double offsetMultiplier, int offsetPeriod, bool showPlot, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.RubyRiverEquivalent(Input, mAType, mAPeriod, mASmoothingEnabled, mASmoothingMethod, mASmoothingPeriod, offsetMultiplier, offsetPeriod, showPlot, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}

		public Indicators.RubyRiverEquivalent RubyRiverEquivalent(ISeries<double> input , RubyRiverMAType mAType, int mAPeriod, bool mASmoothingEnabled, RubyRiverMAType mASmoothingMethod, int mASmoothingPeriod, double offsetMultiplier, int offsetPeriod, bool showPlot, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.RubyRiverEquivalent(input, mAType, mAPeriod, mASmoothingEnabled, mASmoothingMethod, mASmoothingPeriod, offsetMultiplier, offsetPeriod, showPlot, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RubyRiverEquivalent RubyRiverEquivalent(RubyRiverMAType mAType, int mAPeriod, bool mASmoothingEnabled, RubyRiverMAType mASmoothingMethod, int mASmoothingPeriod, double offsetMultiplier, int offsetPeriod, bool showPlot, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.RubyRiverEquivalent(Input, mAType, mAPeriod, mASmoothingEnabled, mASmoothingMethod, mASmoothingPeriod, offsetMultiplier, offsetPeriod, showPlot, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}

		public Indicators.RubyRiverEquivalent RubyRiverEquivalent(ISeries<double> input , RubyRiverMAType mAType, int mAPeriod, bool mASmoothingEnabled, RubyRiverMAType mASmoothingMethod, int mASmoothingPeriod, double offsetMultiplier, int offsetPeriod, bool showPlot, bool showMarkers, string uptrendMarker, string downtrendMarker, int markerOffset)
		{
			return indicator.RubyRiverEquivalent(input, mAType, mAPeriod, mASmoothingEnabled, mASmoothingMethod, mASmoothingPeriod, offsetMultiplier, offsetPeriod, showPlot, showMarkers, uptrendMarker, downtrendMarker, markerOffset);
		}
	}
}

#endregion
