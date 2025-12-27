#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// Enum defined at global NinjaTrader.NinjaScript level for accessibility
namespace NinjaTrader.NinjaScript
{
    public enum T3ProMAType
    {
        SMA,
        EMA,
        DEMA,
        TEMA,
        WMA
    }
}

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// T3 Pro Equivalent - Replicates ninZa T3 Pro functionality
    /// 
    /// Based on discovered template parameters:
    /// - T3 core with configurable MA type, period, TCount, VFactor
    /// - Chaos Smoothing layer (additional smoothing via DEMA/EMA/SMA etc.)
    /// - Trend Filter (ATR-based noise reduction)
    /// - Trend Signal output with direction detection
    /// 
    /// Author: Claude (Anthropic) for Niki1 Strategy Integration
    /// </summary>
    public class T3ProEquivalent : Indicator
    {
        #region Private Variables
        
        // T3 internal EMAs (T3 uses 6 cascaded EMAs)
        private double[] ema1, ema2, ema3, ema4, ema5, ema6;
        private double c1, c2, c3, c4;  // T3 coefficients
        private double alpha;           // EMA alpha
        
        // Chaos Smoothing
        private Series<double> rawT3;
        private Series<double> smoothedT3;
        private Series<double> chaosEma1;
        private Series<double> chaosEma2;
        
        // Filter
        private Series<double> atrSeries;
        private double filteredValue;
        private double prevFilteredValue;
        
        // Trend Detection
        private bool isUptrend;
        private bool prevIsUptrend;
        private int trendBarIndex;
        
        #endregion
        
        #region OnStateChange
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "T3 Pro Equivalent - Replicates ninZa T3 Pro with Chaos Smoothing and Trend Filter";
                Name = "T3ProEquivalent";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;
                
                // T3 Core Parameters (from template)
                MAType = T3ProMAType.EMA;
                Period = 14;
                TCount = 3;
                VFactor = 0.7;
                
                // Chaos Smoothing Parameters (from template)
                ChaosSmoothingEnabled = true;
                ChaosSmoothingMethod = T3ProMAType.DEMA;
                ChaosSmoothingPeriod = 5;
                
                // Filter Parameters (from template)
                FilterEnabled = true;
                FilterMultiplier = 4.0;
                FilterATRPeriod = 14;
                
                // Visual Settings
                PlotEnabled = true;
                PlotUptrend = Brushes.DodgerBlue;
                PlotDowntrend = Brushes.DeepPink;
                
                // Marker Settings
                MarkerEnabled = false;
                MarkerStringUptrend = "Γû▓ + T3";
                MarkerStringDowntrend = "T3 + Γû╝";
                MarkerOffset = 10;
                
                AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "T3");
                AddPlot(new Stroke(Brushes.Transparent, 1), PlotStyle.Line, "Signal_Trend");
            }
            else if (State == State.Configure)
            {
                // Calculate T3 coefficients based on VFactor
                double v = VFactor;
                c1 = -v * v * v;
                c2 = 3 * v * v + 3 * v * v * v;
                c3 = -6 * v * v - 3 * v - 3 * v * v * v;
                c4 = 1 + 3 * v + v * v * v + 3 * v * v;
                
                // EMA alpha
                alpha = 2.0 / (Period + 1);
            }
            else if (State == State.DataLoaded)
            {
                // Initialize arrays for T3 calculation
                ema1 = new double[1];
                ema2 = new double[1];
                ema3 = new double[1];
                ema4 = new double[1];
                ema5 = new double[1];
                ema6 = new double[1];
                
                // Initialize series
                rawT3 = new Series<double>(this);
                smoothedT3 = new Series<double>(this);
                chaosEma1 = new Series<double>(this);
                chaosEma2 = new Series<double>(this);
                atrSeries = new Series<double>(this);
                
                isUptrend = true;
                prevIsUptrend = true;
                trendBarIndex = 0;
            }
        }
        
        #endregion
        
        #region OnBarUpdate
        
        protected override void OnBarUpdate()
        {
            if (CurrentBar < Period)
            {
                rawT3[0] = Close[0];
                smoothedT3[0] = Close[0];
                Values[0][0] = Close[0];
                Values[1][0] = 0;
                return;
            }
            
            // Step 1: Calculate Raw T3
            double t3Value = CalculateT3(Close[0]);
            rawT3[0] = t3Value;
            
            // Step 2: Apply Chaos Smoothing (if enabled)
            double smoothedValue;
            if (ChaosSmoothingEnabled && CurrentBar >= ChaosSmoothingPeriod)
            {
                smoothedValue = ApplyChaosSmoothingValue(t3Value);
            }
            else
            {
                smoothedValue = t3Value;
            }
            smoothedT3[0] = smoothedValue;
            
            // Step 3: Apply Filter (if enabled)
            double finalValue;
            if (FilterEnabled && CurrentBar >= FilterATRPeriod)
            {
                finalValue = ApplyFilter(smoothedValue);
            }
            else
            {
                finalValue = smoothedValue;
                filteredValue = smoothedValue;
            }
            
            // Step 4: Detect Trend
            DetectTrend(finalValue);
            
            // Step 5: Set Plot Values
            Values[0][0] = finalValue;  // T3 value
            Values[1][0] = isUptrend ? 1 : -1;  // Signal_Trend
            
            // Step 6: Color the plot based on trend
            if (PlotEnabled)
            {
                PlotBrushes[0][0] = isUptrend ? PlotUptrend : PlotDowntrend;
            }
            
            // Step 7: Draw markers on trend change
            if (MarkerEnabled && isUptrend != prevIsUptrend)
            {
                if (isUptrend)
                {
                    Draw.Text(this, "T3Up" + CurrentBar, MarkerStringUptrend, 0, 
                        Low[0] - MarkerOffset * TickSize, PlotUptrend);
                }
                else
                {
                    Draw.Text(this, "T3Dn" + CurrentBar, MarkerStringDowntrend, 0, 
                        High[0] + MarkerOffset * TickSize, PlotDowntrend);
                }
            }
            
            prevFilteredValue = filteredValue;
            prevIsUptrend = isUptrend;
        }
        
        #endregion
        
        #region T3 Calculation
        
        private double CalculateT3(double price)
        {
            // T3 is calculated using 6 cascaded EMAs
            // For TCount = 3, we use all 6 EMAs
            // The formula: T3 = c1*e6 + c2*e5 + c3*e4 + c4*e3
            
            if (CurrentBar == 0)
            {
                ema1[0] = price;
                ema2[0] = price;
                ema3[0] = price;
                ema4[0] = price;
                ema5[0] = price;
                ema6[0] = price;
                return price;
            }
            
            // Cascade EMAs based on TCount
            ema1[0] = ema1[0] + alpha * (price - ema1[0]);
            ema2[0] = ema2[0] + alpha * (ema1[0] - ema2[0]);
            
            double result;
            
            if (TCount >= 2)
            {
                ema3[0] = ema3[0] + alpha * (ema2[0] - ema3[0]);
                ema4[0] = ema4[0] + alpha * (ema3[0] - ema4[0]);
            }
            
            if (TCount >= 3)
            {
                ema5[0] = ema5[0] + alpha * (ema4[0] - ema5[0]);
                ema6[0] = ema6[0] + alpha * (ema5[0] - ema6[0]);
            }
            
            // Calculate T3 based on TCount
            switch (TCount)
            {
                case 1:
                    // GD (Generalized DEMA)
                    result = (1 + VFactor) * ema1[0] - VFactor * ema2[0];
                    break;
                case 2:
                    // T2 (Double smoothed)
                    double gd1 = (1 + VFactor) * ema1[0] - VFactor * ema2[0];
                    double gd2 = (1 + VFactor) * ema3[0] - VFactor * ema4[0];
                    result = (1 + VFactor) * gd1 - VFactor * gd2;
                    break;
                case 3:
                default:
                    // T3 (Triple smoothed) - full formula
                    result = c1 * ema6[0] + c2 * ema5[0] + c3 * ema4[0] + c4 * ema3[0];
                    break;
            }
            
            return result;
        }
        
        #endregion
        
        #region Chaos Smoothing
        
        private double ApplyChaosSmoothingValue(double value)
        {
            double smoothAlpha = 2.0 / (ChaosSmoothingPeriod + 1);
            
            if (CurrentBar < ChaosSmoothingPeriod)
            {
                chaosEma1[0] = value;
                chaosEma2[0] = value;
                return value;
            }
            
            switch (ChaosSmoothingMethod)
            {
                case T3ProMAType.SMA:
                    // Simple moving average of rawT3
                    double sum = 0;
                    for (int i = 0; i < ChaosSmoothingPeriod; i++)
                    {
                        sum += rawT3[i];
                    }
                    return sum / ChaosSmoothingPeriod;
                    
                case T3ProMAType.EMA:
                    // Exponential moving average
                    chaosEma1[0] = chaosEma1[1] + smoothAlpha * (value - chaosEma1[1]);
                    return chaosEma1[0];
                    
                case T3ProMAType.DEMA:
                    // Double EMA
                    chaosEma1[0] = chaosEma1[1] + smoothAlpha * (value - chaosEma1[1]);
                    chaosEma2[0] = chaosEma2[1] + smoothAlpha * (chaosEma1[0] - chaosEma2[1]);
                    return 2 * chaosEma1[0] - chaosEma2[0];
                    
                case T3ProMAType.TEMA:
                    // Triple EMA
                    chaosEma1[0] = chaosEma1[1] + smoothAlpha * (value - chaosEma1[1]);
                    chaosEma2[0] = chaosEma2[1] + smoothAlpha * (chaosEma1[0] - chaosEma2[1]);
                    double ema3Val = chaosEma2[1] + smoothAlpha * (chaosEma2[0] - chaosEma2[1]);
                    return 3 * chaosEma1[0] - 3 * chaosEma2[0] + ema3Val;
                    
                case T3ProMAType.WMA:
                    // Weighted moving average
                    double wSum = 0;
                    double wDenom = 0;
                    for (int i = 0; i < ChaosSmoothingPeriod; i++)
                    {
                        double weight = ChaosSmoothingPeriod - i;
                        wSum += rawT3[i] * weight;
                        wDenom += weight;
                    }
                    return wSum / wDenom;
                    
                default:
                    return value;
            }
        }
        
        #endregion
        
        #region Filter
        
        private double ApplyFilter(double value)
        {
            // Calculate ATR for filter threshold
            double tr = Math.Max(High[0] - Low[0], 
                        Math.Max(Math.Abs(High[0] - Close[1]), Math.Abs(Low[0] - Close[1])));
            
            // Simple ATR calculation
            if (CurrentBar < FilterATRPeriod)
            {
                atrSeries[0] = tr;
                filteredValue = value;
                return value;
            }
            
            // EMA of TR for ATR
            double atrAlpha = 2.0 / (FilterATRPeriod + 1);
            atrSeries[0] = atrSeries[1] + atrAlpha * (tr - atrSeries[1]);
            
            double threshold = atrSeries[0] / FilterMultiplier;
            
            // Only update filtered value if change exceeds threshold
            if (Math.Abs(value - filteredValue) > threshold)
            {
                filteredValue = value;
            }
            
            return filteredValue;
        }
        
        #endregion
        
        #region Trend Detection
        
        private void DetectTrend(double value)
        {
            if (CurrentBar < 2)
            {
                isUptrend = true;
                return;
            }
            
            // Compare current value to previous
            double prevValue = Values[0][1];
            
            if (value > prevValue)
            {
                isUptrend = true;
            }
            else if (value < prevValue)
            {
                isUptrend = false;
            }
            // If equal, maintain previous trend
        }
        
        #endregion
        
        #region Properties
        
        [NinjaScriptProperty]
        [Display(Name = "MA Type", Description = "Base moving average type for T3", Order = 1, GroupName = "T3 Core")]
        public T3ProMAType MAType { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Period", Description = "T3 calculation period", Order = 2, GroupName = "T3 Core")]
        public int Period { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 3)]
        [Display(Name = "TCount", Description = "T3 smoothing iterations (1-3)", Order = 3, GroupName = "T3 Core")]
        public int TCount { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "VFactor", Description = "T3 volume/smoothing factor (0-1)", Order = 4, GroupName = "T3 Core")]
        public double VFactor { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Chaos Smoothing Enabled", Description = "Enable additional smoothing layer", Order = 1, GroupName = "Chaos Smoothing")]
        public bool ChaosSmoothingEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Chaos Smoothing Method", Description = "Smoothing method type", Order = 2, GroupName = "Chaos Smoothing")]
        public T3ProMAType ChaosSmoothingMethod { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Chaos Smoothing Period", Description = "Smoothing period", Order = 3, GroupName = "Chaos Smoothing")]
        public int ChaosSmoothingPeriod { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Filter Enabled", Description = "Enable noise filter", Order = 1, GroupName = "Filter")]
        public bool FilterEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.1, 100)]
        [Display(Name = "Filter Multiplier", Description = "Filter strength (higher = less filtering)", Order = 2, GroupName = "Filter")]
        public double FilterMultiplier { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Filter ATR Period", Description = "ATR period for filter calculation", Order = 3, GroupName = "Filter")]
        public int FilterATRPeriod { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Plot Enabled", Description = "Enable plot coloring", Order = 1, GroupName = "Visuals")]
        public bool PlotEnabled { get; set; }
        
        [XmlIgnore]
        [Display(Name = "Plot Uptrend", Description = "Color for uptrend", Order = 2, GroupName = "Visuals")]
        public Brush PlotUptrend { get; set; }
        
        [Browsable(false)]
        public string PlotUptrendSerialize
        {
            get { return Serialize.BrushToString(PlotUptrend); }
            set { PlotUptrend = Serialize.StringToBrush(value); }
        }
        
        [XmlIgnore]
        [Display(Name = "Plot Downtrend", Description = "Color for downtrend", Order = 3, GroupName = "Visuals")]
        public Brush PlotDowntrend { get; set; }
        
        [Browsable(false)]
        public string PlotDowntrendSerialize
        {
            get { return Serialize.BrushToString(PlotDowntrend); }
            set { PlotDowntrend = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Marker Enabled", Description = "Show trend change markers", Order = 1, GroupName = "Markers")]
        public bool MarkerEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Marker Uptrend", Description = "Uptrend marker text", Order = 2, GroupName = "Markers")]
        public string MarkerStringUptrend { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Marker Downtrend", Description = "Downtrend marker text", Order = 3, GroupName = "Markers")]
        public string MarkerStringDowntrend { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Marker Offset", Description = "Marker offset in ticks", Order = 4, GroupName = "Markers")]
        public int MarkerOffset { get; set; }
        
        // Public accessor properties for strategy integration
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> T3Value
        {
            get { return Values[0]; }
        }
        
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Signal_Trend
        {
            get { return Values[1]; }
        }
        
        [Browsable(false)]
        [XmlIgnore]
        public bool IsUptrend
        {
            get { return isUptrend; }
        }
        
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private T3ProEquivalent[] cacheT3ProEquivalent;
		public T3ProEquivalent T3ProEquivalent(T3ProMAType mAType, int period, int tCount, double vFactor, bool chaosSmoothingEnabled, T3ProMAType chaosSmoothingMethod, int chaosSmoothingPeriod, bool filterEnabled, double filterMultiplier, int filterATRPeriod, bool plotEnabled, bool markerEnabled, string markerStringUptrend, string markerStringDowntrend, int markerOffset)
		{
			return T3ProEquivalent(Input, mAType, period, tCount, vFactor, chaosSmoothingEnabled, chaosSmoothingMethod, chaosSmoothingPeriod, filterEnabled, filterMultiplier, filterATRPeriod, plotEnabled, markerEnabled, markerStringUptrend, markerStringDowntrend, markerOffset);
		}

		public T3ProEquivalent T3ProEquivalent(ISeries<double> input, T3ProMAType mAType, int period, int tCount, double vFactor, bool chaosSmoothingEnabled, T3ProMAType chaosSmoothingMethod, int chaosSmoothingPeriod, bool filterEnabled, double filterMultiplier, int filterATRPeriod, bool plotEnabled, bool markerEnabled, string markerStringUptrend, string markerStringDowntrend, int markerOffset)
		{
			if (cacheT3ProEquivalent != null)
				for (int idx = 0; idx < cacheT3ProEquivalent.Length; idx++)
					if (cacheT3ProEquivalent[idx] != null && cacheT3ProEquivalent[idx].MAType == mAType && cacheT3ProEquivalent[idx].Period == period && cacheT3ProEquivalent[idx].TCount == tCount && cacheT3ProEquivalent[idx].VFactor == vFactor && cacheT3ProEquivalent[idx].ChaosSmoothingEnabled == chaosSmoothingEnabled && cacheT3ProEquivalent[idx].ChaosSmoothingMethod == chaosSmoothingMethod && cacheT3ProEquivalent[idx].ChaosSmoothingPeriod == chaosSmoothingPeriod && cacheT3ProEquivalent[idx].FilterEnabled == filterEnabled && cacheT3ProEquivalent[idx].FilterMultiplier == filterMultiplier && cacheT3ProEquivalent[idx].FilterATRPeriod == filterATRPeriod && cacheT3ProEquivalent[idx].PlotEnabled == plotEnabled && cacheT3ProEquivalent[idx].MarkerEnabled == markerEnabled && cacheT3ProEquivalent[idx].MarkerStringUptrend == markerStringUptrend && cacheT3ProEquivalent[idx].MarkerStringDowntrend == markerStringDowntrend && cacheT3ProEquivalent[idx].MarkerOffset == markerOffset && cacheT3ProEquivalent[idx].EqualsInput(input))
						return cacheT3ProEquivalent[idx];
			return CacheIndicator<T3ProEquivalent>(new T3ProEquivalent(){ MAType = mAType, Period = period, TCount = tCount, VFactor = vFactor, ChaosSmoothingEnabled = chaosSmoothingEnabled, ChaosSmoothingMethod = chaosSmoothingMethod, ChaosSmoothingPeriod = chaosSmoothingPeriod, FilterEnabled = filterEnabled, FilterMultiplier = filterMultiplier, FilterATRPeriod = filterATRPeriod, PlotEnabled = plotEnabled, MarkerEnabled = markerEnabled, MarkerStringUptrend = markerStringUptrend, MarkerStringDowntrend = markerStringDowntrend, MarkerOffset = markerOffset }, input, ref cacheT3ProEquivalent);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.T3ProEquivalent T3ProEquivalent(T3ProMAType mAType, int period, int tCount, double vFactor, bool chaosSmoothingEnabled, T3ProMAType chaosSmoothingMethod, int chaosSmoothingPeriod, bool filterEnabled, double filterMultiplier, int filterATRPeriod, bool plotEnabled, bool markerEnabled, string markerStringUptrend, string markerStringDowntrend, int markerOffset)
		{
			return indicator.T3ProEquivalent(Input, mAType, period, tCount, vFactor, chaosSmoothingEnabled, chaosSmoothingMethod, chaosSmoothingPeriod, filterEnabled, filterMultiplier, filterATRPeriod, plotEnabled, markerEnabled, markerStringUptrend, markerStringDowntrend, markerOffset);
		}

		public Indicators.T3ProEquivalent T3ProEquivalent(ISeries<double> input , T3ProMAType mAType, int period, int tCount, double vFactor, bool chaosSmoothingEnabled, T3ProMAType chaosSmoothingMethod, int chaosSmoothingPeriod, bool filterEnabled, double filterMultiplier, int filterATRPeriod, bool plotEnabled, bool markerEnabled, string markerStringUptrend, string markerStringDowntrend, int markerOffset)
		{
			return indicator.T3ProEquivalent(input, mAType, period, tCount, vFactor, chaosSmoothingEnabled, chaosSmoothingMethod, chaosSmoothingPeriod, filterEnabled, filterMultiplier, filterATRPeriod, plotEnabled, markerEnabled, markerStringUptrend, markerStringDowntrend, markerOffset);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.T3ProEquivalent T3ProEquivalent(T3ProMAType mAType, int period, int tCount, double vFactor, bool chaosSmoothingEnabled, T3ProMAType chaosSmoothingMethod, int chaosSmoothingPeriod, bool filterEnabled, double filterMultiplier, int filterATRPeriod, bool plotEnabled, bool markerEnabled, string markerStringUptrend, string markerStringDowntrend, int markerOffset)
		{
			return indicator.T3ProEquivalent(Input, mAType, period, tCount, vFactor, chaosSmoothingEnabled, chaosSmoothingMethod, chaosSmoothingPeriod, filterEnabled, filterMultiplier, filterATRPeriod, plotEnabled, markerEnabled, markerStringUptrend, markerStringDowntrend, markerOffset);
		}

		public Indicators.T3ProEquivalent T3ProEquivalent(ISeries<double> input , T3ProMAType mAType, int period, int tCount, double vFactor, bool chaosSmoothingEnabled, T3ProMAType chaosSmoothingMethod, int chaosSmoothingPeriod, bool filterEnabled, double filterMultiplier, int filterATRPeriod, bool plotEnabled, bool markerEnabled, string markerStringUptrend, string markerStringDowntrend, int markerOffset)
		{
			return indicator.T3ProEquivalent(input, mAType, period, tCount, vFactor, chaosSmoothingEnabled, chaosSmoothingMethod, chaosSmoothingPeriod, filterEnabled, filterMultiplier, filterATRPeriod, plotEnabled, markerEnabled, markerStringUptrend, markerStringDowntrend, markerOffset);
		}
	}
}

#endregion
