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

public enum AIQ1EquivMAMethod
{
    MA1,
    MA2,
    EMA,
    SMA
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AIQ_1Equivalent : Indicator
    {
        #region Private Variables
        // Heiken Ashi values
        private double haOpen, haHigh, haLow, haClose;
        private double prevHaOpen, prevHaClose;
        
        // Smoothed HA values
        private double smoothHaOpen, smoothHaHigh, smoothHaLow, smoothHaClose;
        
        // Trend detection
        private bool isUptrend;
        private bool isBullish;
        private bool isBearish;
        private int trendStrength;
        
        // Momentum
        private double momentum;
        private bool momBull, momBear;
        
        // Signal states
        private bool showBullSquare, showBearSquare;
        private bool showBullDot, showBearDot;
        
        // EMA states for smoothing
        private double emaOpenState, emaHighState, emaLowState, emaCloseState;
        
        // Buffers for SMA
        private double[] openBuffer, highBuffer, lowBuffer, closeBuffer;
        private int bufferIndex;
        
        private bool isInitialized;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"AIQ_1 Equivalent - Heiken Ashi trend indicator with momentum signals";
                Name = "AIQ_1Equivalent";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                
                // Default parameters matching AIQ_1
                Period = 3;
                Phase = 0;
                Method = AIQ1EquivMAMethod.MA1;
                UseBetterFormula = true;
                
                PctAbove = 0.05;
                PctBelow = 0.05;
                SPctAbove = 0.03;
                SPctBelow = 0.03;
                
                // Visual settings
                ShowSquares = true;
                SquareSize = 15;
                SquareOpacity = 100;
                ShowDots = false;
                DotSize = 4;
                
                UpSquareColor = Brushes.Orange;
                DownSquareColor = Brushes.Orange;
                
                // No visible plots by default (matches original with transparent colors)
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                // Initialize buffers
                int bufSize = Period + 1;
                openBuffer = new double[bufSize];
                highBuffer = new double[bufSize];
                lowBuffer = new double[bufSize];
                closeBuffer = new double[bufSize];
                bufferIndex = 0;
                
                isInitialized = false;
                isUptrend = false;
                prevHaOpen = 0;
                prevHaClose = 0;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
            {
                // Initialize first bar
                haOpen = Open[0];
                haClose = (Open[0] + High[0] + Low[0] + Close[0]) / 4;
                haHigh = High[0];
                haLow = Low[0];
                
                prevHaOpen = haOpen;
                prevHaClose = haClose;
                
                smoothHaOpen = haOpen;
                smoothHaHigh = haHigh;
                smoothHaLow = haLow;
                smoothHaClose = haClose;
                
                emaOpenState = haOpen;
                emaHighState = haHigh;
                emaLowState = haLow;
                emaCloseState = haClose;
                
                return;
            }

            // Calculate Heiken Ashi values
            if (UseBetterFormula)
            {
                // Better formula uses previous HA values
                haOpen = (prevHaOpen + prevHaClose) / 2;
                haClose = (Open[0] + High[0] + Low[0] + Close[0]) / 4;
                haHigh = Math.Max(High[0], Math.Max(haOpen, haClose));
                haLow = Math.Min(Low[0], Math.Min(haOpen, haClose));
            }
            else
            {
                // Standard formula
                haOpen = (Open[1] + Close[1]) / 2;
                haClose = (Open[0] + High[0] + Low[0] + Close[0]) / 4;
                haHigh = Math.Max(High[0], Math.Max(haOpen, haClose));
                haLow = Math.Min(Low[0], Math.Min(haOpen, haClose));
            }
            
            // Store for next bar
            prevHaOpen = haOpen;
            prevHaClose = haClose;
            
            // Apply smoothing based on method
            ApplySmoothing();
            
            // Determine trend from smoothed HA
            bool prevUptrend = isUptrend;
            
            // HA candle is bullish if close > open
            isBullish = smoothHaClose > smoothHaOpen;
            isBearish = smoothHaClose < smoothHaOpen;
            
            // Strong trend: no wick on one side
            bool strongBull = isBullish && smoothHaLow >= Math.Min(smoothHaOpen, smoothHaClose);
            bool strongBear = isBearish && smoothHaHigh <= Math.Max(smoothHaOpen, smoothHaClose);
            
            // Update trend state
            if (strongBull)
                isUptrend = true;
            else if (strongBear)
                isUptrend = false;
            else if (isBullish)
                isUptrend = true;
            else if (isBearish)
                isUptrend = false;
            
            // Calculate trend strength (consecutive bars in same direction)
            if (isUptrend == prevUptrend)
                trendStrength = Math.Min(trendStrength + 1, 100);
            else
                trendStrength = 1;
            
            // Calculate momentum
            double priceChange = Close[0] - Close[Math.Min(Period, CurrentBar)];
            double avgPrice = (High[0] + Low[0] + Close[0]) / 3;
            momentum = avgPrice > 0 ? (priceChange / avgPrice) * 100 : 0;
            
            momBull = momentum > PctAbove;
            momBear = momentum < -PctBelow;
            
            // Determine square signals (trend change with momentum confirmation)
            showBullSquare = false;
            showBearSquare = false;
            
            if (isUptrend && !prevUptrend)
            {
                // Trend flip to bullish
                double threshold = SPctAbove / 100.0;
                if (momentum > -threshold || strongBull)
                    showBullSquare = true;
            }
            else if (!isUptrend && prevUptrend)
            {
                // Trend flip to bearish
                double threshold = SPctBelow / 100.0;
                if (momentum < threshold || strongBear)
                    showBearSquare = true;
            }
            
            // Dot signals (momentum breakout within trend)
            showBullDot = isUptrend && momBull && trendStrength >= 2;
            showBearDot = !isUptrend && momBear && trendStrength >= 2;
            
            // Draw squares
            if (ShowSquares)
            {
                if (showBullSquare)
                {
                    Draw.Square(this, "BullSq" + CurrentBar, true, 0, Low[0] - TickSize * SquareSize, UpSquareColor);
                }
                if (showBearSquare)
                {
                    Draw.Square(this, "BearSq" + CurrentBar, true, 0, High[0] + TickSize * SquareSize, DownSquareColor);
                }
            }
            
            // Draw dots
            if (ShowDots)
            {
                if (showBullDot && !showBullSquare)
                {
                    Draw.Dot(this, "BullDot" + CurrentBar, true, 0, Low[0] - TickSize * DotSize, Brushes.LimeGreen);
                }
                if (showBearDot && !showBearSquare)
                {
                    Draw.Dot(this, "BearDot" + CurrentBar, true, 0, High[0] + TickSize * DotSize, Brushes.Red);
                }
            }
            
            if (!isInitialized && CurrentBar >= Period)
                isInitialized = true;
        }
        
        private void ApplySmoothing()
        {
            // Store in buffers
            openBuffer[bufferIndex] = haOpen;
            highBuffer[bufferIndex] = haHigh;
            lowBuffer[bufferIndex] = haLow;
            closeBuffer[bufferIndex] = haClose;
            
            switch (Method)
            {
                case AIQ1EquivMAMethod.MA1:
                case AIQ1EquivMAMethod.SMA:
                    // Simple moving average
                    smoothHaOpen = CalculateSMA(openBuffer);
                    smoothHaHigh = CalculateSMA(highBuffer);
                    smoothHaLow = CalculateSMA(lowBuffer);
                    smoothHaClose = CalculateSMA(closeBuffer);
                    break;
                    
                case AIQ1EquivMAMethod.MA2:
                    // Weighted MA (more weight to recent)
                    smoothHaOpen = CalculateWMA(openBuffer);
                    smoothHaHigh = CalculateWMA(highBuffer);
                    smoothHaLow = CalculateWMA(lowBuffer);
                    smoothHaClose = CalculateWMA(closeBuffer);
                    break;
                    
                case AIQ1EquivMAMethod.EMA:
                    // Exponential MA
                    double k = 2.0 / (Period + 1);
                    emaOpenState = (haOpen - emaOpenState) * k + emaOpenState;
                    emaHighState = (haHigh - emaHighState) * k + emaHighState;
                    emaLowState = (haLow - emaLowState) * k + emaLowState;
                    emaCloseState = (haClose - emaCloseState) * k + emaCloseState;
                    
                    smoothHaOpen = emaOpenState;
                    smoothHaHigh = emaHighState;
                    smoothHaLow = emaLowState;
                    smoothHaClose = emaCloseState;
                    break;
            }
            
            // Apply phase shift if specified
            if (Phase != 0)
            {
                // Phase shifts the values (not commonly used, but supported)
                double shift = Phase * (smoothHaHigh - smoothHaLow) / 100.0;
                smoothHaOpen += shift;
                smoothHaHigh += shift;
                smoothHaLow += shift;
                smoothHaClose += shift;
            }
            
            // Update buffer index
            bufferIndex = (bufferIndex + 1) % openBuffer.Length;
        }
        
        private double CalculateSMA(double[] buffer)
        {
            int count = Math.Min(CurrentBar + 1, Period);
            double sum = 0;
            for (int i = 0; i < count; i++)
                sum += buffer[i];
            return sum / count;
        }
        
        private double CalculateWMA(double[] buffer)
        {
            int count = Math.Min(CurrentBar + 1, Period);
            double sum = 0;
            double weightSum = 0;
            for (int i = 0; i < count; i++)
            {
                int weight = i + 1;
                sum += buffer[i] * weight;
                weightSum += weight;
            }
            return sum / weightSum;
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Period", Description = "Smoothing period", Order = 1, GroupName = "1. Parameters")]
        public int Period { get; set; }

        [NinjaScriptProperty]
        [Range(-100, 100)]
        [Display(Name = "Phase", Description = "Phase shift", Order = 2, GroupName = "1. Parameters")]
        public int Phase { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Method", Description = "Smoothing method", Order = 3, GroupName = "1. Parameters")]
        public AIQ1EquivMAMethod Method { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Better Formula", Description = "Use improved HA formula", Order = 4, GroupName = "1. Parameters")]
        public bool UseBetterFormula { get; set; }

        [NinjaScriptProperty]
        [Range(0.001, 1.0)]
        [Display(Name = "Pct Above", Description = "Percentage threshold for bullish dots", Order = 5, GroupName = "2. Thresholds")]
        public double PctAbove { get; set; }

        [NinjaScriptProperty]
        [Range(0.001, 1.0)]
        [Display(Name = "Pct Below", Description = "Percentage threshold for bearish dots", Order = 6, GroupName = "2. Thresholds")]
        public double PctBelow { get; set; }

        [NinjaScriptProperty]
        [Range(0.001, 1.0)]
        [Display(Name = "Square Pct Above", Description = "Percentage threshold for bullish squares", Order = 7, GroupName = "2. Thresholds")]
        public double SPctAbove { get; set; }

        [NinjaScriptProperty]
        [Range(0.001, 1.0)]
        [Display(Name = "Square Pct Below", Description = "Percentage threshold for bearish squares", Order = 8, GroupName = "2. Thresholds")]
        public double SPctBelow { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Squares", Description = "Show trend change squares", Order = 9, GroupName = "3. Visual")]
        public bool ShowSquares { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Square Size", Description = "Size of squares in ticks", Order = 10, GroupName = "3. Visual")]
        public int SquareSize { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Square Opacity", Description = "Opacity of squares", Order = 11, GroupName = "3. Visual")]
        public int SquareOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Dots", Description = "Show momentum dots", Order = 12, GroupName = "3. Visual")]
        public bool ShowDots { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Dot Size", Description = "Size of dots in ticks", Order = 13, GroupName = "3. Visual")]
        public int DotSize { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Up Square Color", Description = "Color for bullish squares", Order = 14, GroupName = "3. Visual")]
        public Brush UpSquareColor { get; set; }

        [Browsable(false)]
        public string UpSquareColorSerializable
        {
            get { return Serialize.BrushToString(UpSquareColor); }
            set { UpSquareColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Down Square Color", Description = "Color for bearish squares", Order = 15, GroupName = "3. Visual")]
        public Brush DownSquareColor { get; set; }

        [Browsable(false)]
        public string DownSquareColorSerializable
        {
            get { return Serialize.BrushToString(DownSquareColor); }
            set { DownSquareColor = Serialize.StringToBrush(value); }
        }

        // Public properties for strategy access
        [Browsable(false)]
        public bool IsUptrend
        {
            get { return isUptrend; }
        }
        
        [Browsable(false)]
        public bool IsBullish
        {
            get { return isBullish; }
        }
        
        [Browsable(false)]
        public bool IsBearish
        {
            get { return isBearish; }
        }
        
        [Browsable(false)]
        public int TrendStrength
        {
            get { return trendStrength; }
        }
        
        [Browsable(false)]
        public double Momentum
        {
            get { return momentum; }
        }
        
        [Browsable(false)]
        public bool MomentumBull
        {
            get { return momBull; }
        }
        
        [Browsable(false)]
        public bool MomentumBear
        {
            get { return momBear; }
        }
        
        [Browsable(false)]
        public double HaOpen
        {
            get { return smoothHaOpen; }
        }
        
        [Browsable(false)]
        public double HaHigh
        {
            get { return smoothHaHigh; }
        }
        
        [Browsable(false)]
        public double HaLow
        {
            get { return smoothHaLow; }
        }
        
        [Browsable(false)]
        public double HaClose
        {
            get { return smoothHaClose; }
        }
        
        [Browsable(false)]
        public bool ShowBullSquare
        {
            get { return showBullSquare; }
        }
        
        [Browsable(false)]
        public bool ShowBearSquare
        {
            get { return showBearSquare; }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AIQ_1Equivalent[] cacheAIQ_1Equivalent;
		public AIQ_1Equivalent AIQ_1Equivalent(int period, int phase, AIQ1EquivMAMethod method, bool useBetterFormula, double pctAbove, double pctBelow, double sPctAbove, double sPctBelow, bool showSquares, int squareSize, int squareOpacity, bool showDots, int dotSize, Brush upSquareColor, Brush downSquareColor)
		{
			return AIQ_1Equivalent(Input, period, phase, method, useBetterFormula, pctAbove, pctBelow, sPctAbove, sPctBelow, showSquares, squareSize, squareOpacity, showDots, dotSize, upSquareColor, downSquareColor);
		}

		public AIQ_1Equivalent AIQ_1Equivalent(ISeries<double> input, int period, int phase, AIQ1EquivMAMethod method, bool useBetterFormula, double pctAbove, double pctBelow, double sPctAbove, double sPctBelow, bool showSquares, int squareSize, int squareOpacity, bool showDots, int dotSize, Brush upSquareColor, Brush downSquareColor)
		{
			if (cacheAIQ_1Equivalent != null)
				for (int idx = 0; idx < cacheAIQ_1Equivalent.Length; idx++)
					if (cacheAIQ_1Equivalent[idx] != null && cacheAIQ_1Equivalent[idx].Period == period && cacheAIQ_1Equivalent[idx].Phase == phase && cacheAIQ_1Equivalent[idx].Method == method && cacheAIQ_1Equivalent[idx].UseBetterFormula == useBetterFormula && cacheAIQ_1Equivalent[idx].PctAbove == pctAbove && cacheAIQ_1Equivalent[idx].PctBelow == pctBelow && cacheAIQ_1Equivalent[idx].SPctAbove == sPctAbove && cacheAIQ_1Equivalent[idx].SPctBelow == sPctBelow && cacheAIQ_1Equivalent[idx].ShowSquares == showSquares && cacheAIQ_1Equivalent[idx].SquareSize == squareSize && cacheAIQ_1Equivalent[idx].SquareOpacity == squareOpacity && cacheAIQ_1Equivalent[idx].ShowDots == showDots && cacheAIQ_1Equivalent[idx].DotSize == dotSize && cacheAIQ_1Equivalent[idx].UpSquareColor == upSquareColor && cacheAIQ_1Equivalent[idx].DownSquareColor == downSquareColor && cacheAIQ_1Equivalent[idx].EqualsInput(input))
						return cacheAIQ_1Equivalent[idx];
			return CacheIndicator<AIQ_1Equivalent>(new AIQ_1Equivalent(){ Period = period, Phase = phase, Method = method, UseBetterFormula = useBetterFormula, PctAbove = pctAbove, PctBelow = pctBelow, SPctAbove = sPctAbove, SPctBelow = sPctBelow, ShowSquares = showSquares, SquareSize = squareSize, SquareOpacity = squareOpacity, ShowDots = showDots, DotSize = dotSize, UpSquareColor = upSquareColor, DownSquareColor = downSquareColor }, input, ref cacheAIQ_1Equivalent);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AIQ_1Equivalent AIQ_1Equivalent(int period, int phase, AIQ1EquivMAMethod method, bool useBetterFormula, double pctAbove, double pctBelow, double sPctAbove, double sPctBelow, bool showSquares, int squareSize, int squareOpacity, bool showDots, int dotSize, Brush upSquareColor, Brush downSquareColor)
		{
			return indicator.AIQ_1Equivalent(Input, period, phase, method, useBetterFormula, pctAbove, pctBelow, sPctAbove, sPctBelow, showSquares, squareSize, squareOpacity, showDots, dotSize, upSquareColor, downSquareColor);
		}

		public Indicators.AIQ_1Equivalent AIQ_1Equivalent(ISeries<double> input , int period, int phase, AIQ1EquivMAMethod method, bool useBetterFormula, double pctAbove, double pctBelow, double sPctAbove, double sPctBelow, bool showSquares, int squareSize, int squareOpacity, bool showDots, int dotSize, Brush upSquareColor, Brush downSquareColor)
		{
			return indicator.AIQ_1Equivalent(input, period, phase, method, useBetterFormula, pctAbove, pctBelow, sPctAbove, sPctBelow, showSquares, squareSize, squareOpacity, showDots, dotSize, upSquareColor, downSquareColor);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AIQ_1Equivalent AIQ_1Equivalent(int period, int phase, AIQ1EquivMAMethod method, bool useBetterFormula, double pctAbove, double pctBelow, double sPctAbove, double sPctBelow, bool showSquares, int squareSize, int squareOpacity, bool showDots, int dotSize, Brush upSquareColor, Brush downSquareColor)
		{
			return indicator.AIQ_1Equivalent(Input, period, phase, method, useBetterFormula, pctAbove, pctBelow, sPctAbove, sPctBelow, showSquares, squareSize, squareOpacity, showDots, dotSize, upSquareColor, downSquareColor);
		}

		public Indicators.AIQ_1Equivalent AIQ_1Equivalent(ISeries<double> input , int period, int phase, AIQ1EquivMAMethod method, bool useBetterFormula, double pctAbove, double pctBelow, double sPctAbove, double sPctBelow, bool showSquares, int squareSize, int squareOpacity, bool showDots, int dotSize, Brush upSquareColor, Brush downSquareColor)
		{
			return indicator.AIQ_1Equivalent(input, period, phase, method, useBetterFormula, pctAbove, pctBelow, sPctAbove, sPctBelow, showSquares, squareSize, squareOpacity, showDots, dotSize, upSquareColor, downSquareColor);
		}
	}
}

#endregion
