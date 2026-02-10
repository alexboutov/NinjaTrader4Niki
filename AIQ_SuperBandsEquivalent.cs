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

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AIQ_SuperBandsEquivalent : Indicator
    {
        #region Private Variables
        // Main bands (slow)
        private double mainUpper, mainMiddle, mainLower;
        private double mainStdDev;
        
        // Fast bands
        private double fastUpper, fastMiddle, fastLower;
        private double fastStdDev;
        
        // Circular buffers for calculations
        private double[] mainPriceBuffer;
        private double[] fastPriceBuffer;
        private int mainBufferIndex, fastBufferIndex;
        
        // Signal states
        private bool bullishSignal, bearishSignal;
        private bool priceAboveMain, priceBelowMain;
        private bool priceAboveFast, priceBelowFast;
        
        // Optimized deviation
        private double optimizedDeviation;
        private int outOfBandCount;
        private int totalBarsTracked;
        
        private bool isInitialized;
        
        // Trend state for strategy integration
        private bool isUptrend;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"AIQ SuperBands Equivalent - Volatility bands with dual timeframes";
                Name = "AIQ_SuperBandsEquivalent";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                ScaleJustification = ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                
                // Default parameters matching AIQ_SuperBands
                HalfLength_Main = 101;
                BandsDeviation_Main = 2.5;
                HalfLength_Fast = 11;
                BandsDeviation_Fast = 3.0;
                
                StaticBands = false;
                OptimizeMainDeviation = true;
                MaxOutOfBandPercent = 7;
                
                PctAbove = 0.05;
                PctBelow = 0.05;
                
                // Visual settings
                EnableMainBands = true;
                EnableFastBands = false;
                EnableTriangles = true;
                EnableLines = true;
                
                MainBandColor = Brushes.LightPink;
                MainMiddleColor = Brushes.White;
                FastBandColor = Brushes.Yellow;
                
                // Add plots for main bands
                AddPlot(new Stroke(Brushes.LightPink, DashStyleHelper.DashDot, 5), PlotStyle.Line, "MainUpper");
                AddPlot(new Stroke(Brushes.White, DashStyleHelper.Dot, 3), PlotStyle.Line, "MainMiddle");
                AddPlot(new Stroke(Brushes.LightPink, DashStyleHelper.DashDot, 5), PlotStyle.Line, "MainLower");
                
                // Add plots for fast bands (disabled by default via transparent)
                AddPlot(new Stroke(Brushes.Transparent, 3), PlotStyle.Line, "FastUpper");
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Dash, 1), PlotStyle.Line, "FastMiddle");
                AddPlot(new Stroke(Brushes.Transparent, 3), PlotStyle.Line, "FastLower");
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                // Initialize buffers - use full length for proper std dev calculation
                int mainLength = HalfLength_Main * 2 + 1;
                int fastLength = HalfLength_Fast * 2 + 1;
                
                mainPriceBuffer = new double[mainLength];
                fastPriceBuffer = new double[fastLength];
                
                mainBufferIndex = 0;
                fastBufferIndex = 0;
                
                isInitialized = false;
                optimizedDeviation = BandsDeviation_Main;
                outOfBandCount = 0;
                totalBarsTracked = 0;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
            {
                mainMiddle = Close[0];
                mainUpper = Close[0];
                mainLower = Close[0];
                fastMiddle = Close[0];
                fastUpper = Close[0];
                fastLower = Close[0];
                
                Values[0][0] = mainUpper;
                Values[1][0] = mainMiddle;
                Values[2][0] = mainLower;
                Values[3][0] = fastUpper;
                Values[4][0] = fastMiddle;
                Values[5][0] = fastLower;
                return;
            }

            // Update price buffers
            mainPriceBuffer[mainBufferIndex] = Close[0];
            fastPriceBuffer[fastBufferIndex] = Close[0];
            
            // Calculate main bands (slow)
            int mainPeriod = Math.Min(CurrentBar + 1, HalfLength_Main * 2 + 1);
            CalculateBands(mainPriceBuffer, mainPeriod, mainBufferIndex, 
                           out mainMiddle, out mainStdDev);
            
            // Calculate fast bands
            int fastPeriod = Math.Min(CurrentBar + 1, HalfLength_Fast * 2 + 1);
            CalculateBands(fastPriceBuffer, fastPeriod, fastBufferIndex,
                           out fastMiddle, out fastStdDev);
            
            // Optimize deviation if enabled
            if (OptimizeMainDeviation && CurrentBar >= HalfLength_Main * 2)
            {
                totalBarsTracked++;
                
                // Check if price is outside bands
                double tempUpper = mainMiddle + optimizedDeviation * mainStdDev;
                double tempLower = mainMiddle - optimizedDeviation * mainStdDev;
                
                if (Close[0] > tempUpper || Close[0] < tempLower)
                    outOfBandCount++;
                
                // Adjust deviation to target MaxOutOfBandPercent
                if (totalBarsTracked >= 20)
                {
                    double actualPercent = (double)outOfBandCount / totalBarsTracked * 100;
                    double targetPercent = MaxOutOfBandPercent;
                    
                    if (actualPercent > targetPercent + 1)
                        optimizedDeviation *= 1.01; // Widen bands
                    else if (actualPercent < targetPercent - 1 && optimizedDeviation > 1.0)
                        optimizedDeviation *= 0.99; // Narrow bands
                    
                    // Clamp to reasonable range
                    optimizedDeviation = Math.Max(1.0, Math.Min(5.0, optimizedDeviation));
                }
            }
            else
            {
                optimizedDeviation = BandsDeviation_Main;
            }
            
            // Calculate final band values
            double mainDev = StaticBands ? BandsDeviation_Main : optimizedDeviation;
            mainUpper = mainMiddle + mainDev * mainStdDev;
            mainLower = mainMiddle - mainDev * mainStdDev;
            
            fastUpper = fastMiddle + BandsDeviation_Fast * fastStdDev;
            fastLower = fastMiddle - BandsDeviation_Fast * fastStdDev;
            
            // Update plot values
            Values[0][0] = mainUpper;
            Values[1][0] = mainMiddle;
            Values[2][0] = mainLower;
            Values[3][0] = fastUpper;
            Values[4][0] = fastMiddle;
            Values[5][0] = fastLower;
            
            // Determine trend state: bullish when price is above main middle band
            isUptrend = Close[0] > mainMiddle;
            
            // Determine price position relative to bands
            bool prevAboveMain = priceAboveMain;
            bool prevBelowMain = priceBelowMain;
            bool prevAboveFast = priceAboveFast;
            bool prevBelowFast = priceBelowFast;
            
            priceAboveMain = Close[0] > mainUpper;
            priceBelowMain = Close[0] < mainLower;
            priceAboveFast = Close[0] > fastUpper;
            priceBelowFast = Close[0] < fastLower;
            
            // Detect signals based on band crossings
            bullishSignal = false;
            bearishSignal = false;
            
            // Bullish: Price crosses above lower band (reversal from oversold)
            // Or price breaks above upper band (momentum breakout)
            if (priceBelowMain && !prevBelowMain)
            {
                // Price just went below lower band - potential reversal setup
            }
            else if (!priceBelowMain && prevBelowMain)
            {
                // Price crossed back above lower band - bullish reversal
                bullishSignal = true;
            }
            else if (priceAboveMain && !prevAboveMain)
            {
                // Price broke above upper band - bullish momentum
                bullishSignal = true;
            }
            
            // Bearish: Price crosses below upper band (reversal from overbought)
            // Or price breaks below lower band (momentum breakdown)
            if (priceAboveMain && !prevAboveMain)
            {
                // Price just went above upper band - potential reversal setup
            }
            else if (!priceAboveMain && prevAboveMain)
            {
                // Price crossed back below upper band - bearish reversal
                bearishSignal = true;
            }
            else if (priceBelowMain && !prevBelowMain)
            {
                // Price broke below lower band - bearish momentum
                bearishSignal = true;
            }
            
            // Draw triangles if enabled
            if (EnableTriangles && CurrentBar > HalfLength_Main)
            {
                if (bullishSignal)
                {
                    Draw.TriangleUp(this, "BullTri" + CurrentBar, true, 0, 
                                    Low[0] - TickSize * 15, Brushes.LimeGreen);
                }
                if (bearishSignal)
                {
                    Draw.TriangleDown(this, "BearTri" + CurrentBar, true, 0,
                                      High[0] + TickSize * 15, Brushes.Red);
                }
            }
            
            // Update buffer indices
            mainBufferIndex = (mainBufferIndex + 1) % mainPriceBuffer.Length;
            fastBufferIndex = (fastBufferIndex + 1) % fastPriceBuffer.Length;
            
            if (!isInitialized && CurrentBar >= HalfLength_Main * 2)
                isInitialized = true;
        }
        
        private void CalculateBands(double[] buffer, int period, int currentIndex,
                                    out double middle, out double stdDev)
        {
            // Calculate weighted moving average (triangular weighting for "HalfLength")
            // This creates smoother bands than simple SMA
            double sum = 0;
            double weightSum = 0;
            int halfPeriod = period / 2;
            
            for (int i = 0; i < period; i++)
            {
                int idx = (currentIndex - i + buffer.Length) % buffer.Length;
                if (idx < 0) idx += buffer.Length;
                
                // Triangular weight - highest in middle, tapering at ends
                int distFromCenter = Math.Abs(i - halfPeriod);
                double weight = halfPeriod - distFromCenter + 1;
                
                sum += buffer[idx] * weight;
                weightSum += weight;
            }
            
            middle = sum / weightSum;
            
            // Calculate standard deviation
            double sqSum = 0;
            for (int i = 0; i < period; i++)
            {
                int idx = (currentIndex - i + buffer.Length) % buffer.Length;
                if (idx < 0) idx += buffer.Length;
                
                double diff = buffer[idx] - middle;
                sqSum += diff * diff;
            }
            
            stdDev = Math.Sqrt(sqSum / period);
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Main Half Length", Description = "Half period for main bands", Order = 1, GroupName = "1. Main Bands")]
        public int HalfLength_Main { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "Main Deviation", Description = "Standard deviation multiplier for main bands", Order = 2, GroupName = "1. Main Bands")]
        public double BandsDeviation_Main { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Fast Half Length", Description = "Half period for fast bands", Order = 3, GroupName = "2. Fast Bands")]
        public int HalfLength_Fast { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10.0)]
        [Display(Name = "Fast Deviation", Description = "Standard deviation multiplier for fast bands", Order = 4, GroupName = "2. Fast Bands")]
        public double BandsDeviation_Fast { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Static Bands", Description = "Use fixed deviation (no optimization)", Order = 5, GroupName = "3. Optimization")]
        public bool StaticBands { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Optimize Main Deviation", Description = "Auto-adjust deviation to contain price", Order = 6, GroupName = "3. Optimization")]
        public bool OptimizeMainDeviation { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Max Out of Band %", Description = "Target percentage of bars outside bands", Order = 7, GroupName = "3. Optimization")]
        public int MaxOutOfBandPercent { get; set; }

        [NinjaScriptProperty]
        [Range(0.001, 1.0)]
        [Display(Name = "Pct Above", Description = "Percentage threshold above", Order = 8, GroupName = "4. Thresholds")]
        public double PctAbove { get; set; }

        [NinjaScriptProperty]
        [Range(0.001, 1.0)]
        [Display(Name = "Pct Below", Description = "Percentage threshold below", Order = 9, GroupName = "4. Thresholds")]
        public double PctBelow { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Main Bands", Description = "Show main band lines", Order = 10, GroupName = "5. Visual")]
        public bool EnableMainBands { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Fast Bands", Description = "Show fast band lines", Order = 11, GroupName = "5. Visual")]
        public bool EnableFastBands { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Triangles", Description = "Show signal triangles", Order = 12, GroupName = "5. Visual")]
        public bool EnableTriangles { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Lines", Description = "Show band lines", Order = 13, GroupName = "5. Visual")]
        public bool EnableLines { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Main Band Color", Description = "Color for main bands", Order = 14, GroupName = "5. Visual")]
        public Brush MainBandColor { get; set; }

        [Browsable(false)]
        public string MainBandColorSerializable
        {
            get { return Serialize.BrushToString(MainBandColor); }
            set { MainBandColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Main Middle Color", Description = "Color for main middle line", Order = 15, GroupName = "5. Visual")]
        public Brush MainMiddleColor { get; set; }

        [Browsable(false)]
        public string MainMiddleColorSerializable
        {
            get { return Serialize.BrushToString(MainMiddleColor); }
            set { MainMiddleColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Fast Band Color", Description = "Color for fast bands", Order = 16, GroupName = "5. Visual")]
        public Brush FastBandColor { get; set; }

        [Browsable(false)]
        public string FastBandColorSerializable
        {
            get { return Serialize.BrushToString(FastBandColor); }
            set { FastBandColor = Serialize.StringToBrush(value); }
        }

        // Public properties for strategy/indicator access
        [Browsable(false)]
        public double MainUpper
        {
            get { return mainUpper; }
        }
        
        [Browsable(false)]
        public double MainMiddle
        {
            get { return mainMiddle; }
        }
        
        [Browsable(false)]
        public double MainLower
        {
            get { return mainLower; }
        }
        
        [Browsable(false)]
        public double FastUpper
        {
            get { return fastUpper; }
        }
        
        [Browsable(false)]
        public double FastMiddle
        {
            get { return fastMiddle; }
        }
        
        [Browsable(false)]
        public double FastLower
        {
            get { return fastLower; }
        }
        
        [Browsable(false)]
        public bool PriceAboveMainUpper
        {
            get { return priceAboveMain; }
        }
        
        [Browsable(false)]
        public bool PriceBelowMainLower
        {
            get { return priceBelowMain; }
        }
        
        [Browsable(false)]
        public bool PriceAboveFastUpper
        {
            get { return priceAboveFast; }
        }
        
        [Browsable(false)]
        public bool PriceBelowFastLower
        {
            get { return priceBelowFast; }
        }
        
        [Browsable(false)]
        public bool BullishSignal
        {
            get { return bullishSignal; }
        }
        
        [Browsable(false)]
        public bool BearishSignal
        {
            get { return bearishSignal; }
        }
        
        /// <summary>
        /// Returns true when price is above the main middle band (bullish bias)
        /// Used by ActiveNikiTrader strategy for confluence detection
        /// </summary>
        [Browsable(false)]
        public bool IsUptrend
        {
            get { return isUptrend; }
        }
        
        [Browsable(false)]
        public double OptimizedDeviation
        {
            get { return optimizedDeviation; }
        }
        
        [Browsable(false)]
        public double MainStdDev
        {
            get { return mainStdDev; }
        }
        
        [Browsable(false)]
        public double FastStdDev
        {
            get { return fastStdDev; }
        }
        #endregion
    }
}
