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
    /// <summary>
    /// AAATrendSync Equivalent - Detects when Fast, Mid, and Slow MAs are in sync
    /// Uptrend: Fast > Mid > Slow (all aligned bullish)
    /// Downtrend: Fast < Mid < Slow (all aligned bearish)
    /// </summary>
    public class AAATrendSyncEquivalent : Indicator
    {
        private Series<double> fastMA;
        private Series<double> midMA;
        private Series<double> slowMA;
        
        // For smoothing
        private EMA fastSmoother;
        private EMA midSmoother;
        private EMA slowSmoother;
        
        // For minimum spread filter
        private ATR atrIndicator;
        
        // State tracking
        private bool isUptrend;
        private bool isDowntrend;
        private bool isSynced;
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "AAATrendSync Equivalent - Detects synchronized trend across Fast/Mid/Slow MAs";
                Name = "AAATrendSyncEquivalent";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;
                
                // Fast MA settings
                FastPeriod = 10;
                FastSmoothingEnabled = true;
                FastSmoothingPeriod = 2;
                
                // Mid MA settings
                MidPeriod = 20;
                MidSmoothingEnabled = true;
                MidSmoothingPeriod = 2;
                
                // Slow MA settings
                SlowPeriod = 30;
                SlowSmoothingEnabled = true;
                SlowSmoothingPeriod = 5;
                
                // Spread filter
                MinSpreadEnabled = true;
                MinSpreadATRMultiplier = 0.05;
                MinSpreadATRPeriod = 100;
                
                // Visual settings
                ShowMarkers = true;
                MarkerOffset = 10;
                UptrendMarker = "▲";
                DowntrendMarker = "▼";
                
                AddPlot(Brushes.Transparent, "SyncState");  // Hidden plot for state value
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                fastMA = new Series<double>(this);
                midMA = new Series<double>(this);
                slowMA = new Series<double>(this);
                
                // Create smoothers if enabled
                if (FastSmoothingEnabled)
                    fastSmoother = EMA(EMA(Close, FastPeriod), FastSmoothingPeriod);
                
                if (MidSmoothingEnabled)
                    midSmoother = EMA(EMA(Close, MidPeriod), MidSmoothingPeriod);
                
                if (SlowSmoothingEnabled)
                    slowSmoother = EMA(EMA(Close, SlowPeriod), SlowSmoothingPeriod);
                
                if (MinSpreadEnabled)
                    atrIndicator = ATR(MinSpreadATRPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(SlowPeriod, MinSpreadATRPeriod) + 10)
                return;
            
            // Calculate MAs (with optional smoothing)
            double fast = FastSmoothingEnabled ? fastSmoother[0] : EMA(Close, FastPeriod)[0];
            double mid = MidSmoothingEnabled ? midSmoother[0] : EMA(Close, MidPeriod)[0];
            double slow = SlowSmoothingEnabled ? slowSmoother[0] : EMA(Close, SlowPeriod)[0];
            
            fastMA[0] = fast;
            midMA[0] = mid;
            slowMA[0] = slow;
            
            // Check minimum spread if enabled
            bool spreadOK = true;
            if (MinSpreadEnabled && atrIndicator != null)
            {
                double minSpread = atrIndicator[0] * MinSpreadATRMultiplier;
                double fastMidSpread = Math.Abs(fast - mid);
                spreadOK = fastMidSpread >= minSpread;
            }
            
            // Determine sync state
            bool wasUptrend = isUptrend;
            bool wasDowntrend = isDowntrend;
            
            // Uptrend: Fast > Mid > Slow (all aligned bullish)
            isUptrend = spreadOK && fast > mid && mid > slow;
            
            // Downtrend: Fast < Mid < Slow (all aligned bearish)
            isDowntrend = spreadOK && fast < mid && mid < slow;
            
            isSynced = isUptrend || isDowntrend;
            
            // Set plot value for state
            Value[0] = isUptrend ? 1 : (isDowntrend ? -1 : 0);
            
            // Draw markers on transitions
            if (ShowMarkers)
            {
                if (isUptrend && !wasUptrend)
                {
                    Draw.Text(this, "Up" + CurrentBar, UptrendMarker, 0, Low[0] - MarkerOffset * TickSize, Brushes.DodgerBlue);
                }
                else if (isDowntrend && !wasDowntrend)
                {
                    Draw.Text(this, "Dn" + CurrentBar, DowntrendMarker, 0, High[0] + MarkerOffset * TickSize, Brushes.Crimson);
                }
            }
        }
        
        #region Properties
        
        [Browsable(false)]
        [XmlIgnore]
        public bool IsUptrend
        {
            get { return isUptrend; }
        }
        
        [Browsable(false)]
        [XmlIgnore]
        public bool IsDowntrend
        {
            get { return isDowntrend; }
        }
        
        [Browsable(false)]
        [XmlIgnore]
        public bool IsSynced
        {
            get { return isSynced; }
        }
        
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> SyncState
        {
            get { return Value; }
        }
        
        // Fast MA Parameters
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Fast Period", Order = 1, GroupName = "1. Fast MA")]
        public int FastPeriod { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Fast Smoothing Enabled", Order = 2, GroupName = "1. Fast MA")]
        public bool FastSmoothingEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Fast Smoothing Period", Order = 3, GroupName = "1. Fast MA")]
        public int FastSmoothingPeriod { get; set; }
        
        // Mid MA Parameters
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Mid Period", Order = 1, GroupName = "2. Mid MA")]
        public int MidPeriod { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Mid Smoothing Enabled", Order = 2, GroupName = "2. Mid MA")]
        public bool MidSmoothingEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Mid Smoothing Period", Order = 3, GroupName = "2. Mid MA")]
        public int MidSmoothingPeriod { get; set; }
        
        // Slow MA Parameters
        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Slow Period", Order = 1, GroupName = "3. Slow MA")]
        public int SlowPeriod { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Slow Smoothing Enabled", Order = 2, GroupName = "3. Slow MA")]
        public bool SlowSmoothingEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Slow Smoothing Period", Order = 3, GroupName = "3. Slow MA")]
        public int SlowSmoothingPeriod { get; set; }
        
        // Spread Filter
        [NinjaScriptProperty]
        [Display(Name = "Min Spread Enabled", Order = 1, GroupName = "4. Spread Filter")]
        public bool MinSpreadEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 1.0)]
        [Display(Name = "Min Spread ATR Multiplier", Order = 2, GroupName = "4. Spread Filter")]
        public double MinSpreadATRMultiplier { get; set; }
        
        [NinjaScriptProperty]
        [Range(10, 200)]
        [Display(Name = "Min Spread ATR Period", Order = 3, GroupName = "4. Spread Filter")]
        public int MinSpreadATRPeriod { get; set; }
        
        // Visual Settings
        [NinjaScriptProperty]
        [Display(Name = "Show Markers", Order = 1, GroupName = "5. Display")]
        public bool ShowMarkers { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Marker Offset", Order = 2, GroupName = "5. Display")]
        public int MarkerOffset { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Uptrend Marker", Order = 3, GroupName = "5. Display")]
        public string UptrendMarker { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Downtrend Marker", Order = 4, GroupName = "5. Display")]
        public string DowntrendMarker { get; set; }
        
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AAATrendSyncEquivalent[] cacheAAATrendSyncEquivalent;
		public AAATrendSyncEquivalent AAATrendSyncEquivalent(int fastPeriod, bool fastSmoothingEnabled, int fastSmoothingPeriod, int midPeriod, bool midSmoothingEnabled, int midSmoothingPeriod, int slowPeriod, bool slowSmoothingEnabled, int slowSmoothingPeriod, bool minSpreadEnabled, double minSpreadATRMultiplier, int minSpreadATRPeriod, bool showMarkers, int markerOffset, string uptrendMarker, string downtrendMarker)
		{
			return AAATrendSyncEquivalent(Input, fastPeriod, fastSmoothingEnabled, fastSmoothingPeriod, midPeriod, midSmoothingEnabled, midSmoothingPeriod, slowPeriod, slowSmoothingEnabled, slowSmoothingPeriod, minSpreadEnabled, minSpreadATRMultiplier, minSpreadATRPeriod, showMarkers, markerOffset, uptrendMarker, downtrendMarker);
		}

		public AAATrendSyncEquivalent AAATrendSyncEquivalent(ISeries<double> input, int fastPeriod, bool fastSmoothingEnabled, int fastSmoothingPeriod, int midPeriod, bool midSmoothingEnabled, int midSmoothingPeriod, int slowPeriod, bool slowSmoothingEnabled, int slowSmoothingPeriod, bool minSpreadEnabled, double minSpreadATRMultiplier, int minSpreadATRPeriod, bool showMarkers, int markerOffset, string uptrendMarker, string downtrendMarker)
		{
			if (cacheAAATrendSyncEquivalent != null)
				for (int idx = 0; idx < cacheAAATrendSyncEquivalent.Length; idx++)
					if (cacheAAATrendSyncEquivalent[idx] != null && cacheAAATrendSyncEquivalent[idx].FastPeriod == fastPeriod && cacheAAATrendSyncEquivalent[idx].FastSmoothingEnabled == fastSmoothingEnabled && cacheAAATrendSyncEquivalent[idx].FastSmoothingPeriod == fastSmoothingPeriod && cacheAAATrendSyncEquivalent[idx].MidPeriod == midPeriod && cacheAAATrendSyncEquivalent[idx].MidSmoothingEnabled == midSmoothingEnabled && cacheAAATrendSyncEquivalent[idx].MidSmoothingPeriod == midSmoothingPeriod && cacheAAATrendSyncEquivalent[idx].SlowPeriod == slowPeriod && cacheAAATrendSyncEquivalent[idx].SlowSmoothingEnabled == slowSmoothingEnabled && cacheAAATrendSyncEquivalent[idx].SlowSmoothingPeriod == slowSmoothingPeriod && cacheAAATrendSyncEquivalent[idx].MinSpreadEnabled == minSpreadEnabled && cacheAAATrendSyncEquivalent[idx].MinSpreadATRMultiplier == minSpreadATRMultiplier && cacheAAATrendSyncEquivalent[idx].MinSpreadATRPeriod == minSpreadATRPeriod && cacheAAATrendSyncEquivalent[idx].ShowMarkers == showMarkers && cacheAAATrendSyncEquivalent[idx].MarkerOffset == markerOffset && cacheAAATrendSyncEquivalent[idx].UptrendMarker == uptrendMarker && cacheAAATrendSyncEquivalent[idx].DowntrendMarker == downtrendMarker && cacheAAATrendSyncEquivalent[idx].EqualsInput(input))
						return cacheAAATrendSyncEquivalent[idx];
			return CacheIndicator<AAATrendSyncEquivalent>(new AAATrendSyncEquivalent(){ FastPeriod = fastPeriod, FastSmoothingEnabled = fastSmoothingEnabled, FastSmoothingPeriod = fastSmoothingPeriod, MidPeriod = midPeriod, MidSmoothingEnabled = midSmoothingEnabled, MidSmoothingPeriod = midSmoothingPeriod, SlowPeriod = slowPeriod, SlowSmoothingEnabled = slowSmoothingEnabled, SlowSmoothingPeriod = slowSmoothingPeriod, MinSpreadEnabled = minSpreadEnabled, MinSpreadATRMultiplier = minSpreadATRMultiplier, MinSpreadATRPeriod = minSpreadATRPeriod, ShowMarkers = showMarkers, MarkerOffset = markerOffset, UptrendMarker = uptrendMarker, DowntrendMarker = downtrendMarker }, input, ref cacheAAATrendSyncEquivalent);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AAATrendSyncEquivalent AAATrendSyncEquivalent(int fastPeriod, bool fastSmoothingEnabled, int fastSmoothingPeriod, int midPeriod, bool midSmoothingEnabled, int midSmoothingPeriod, int slowPeriod, bool slowSmoothingEnabled, int slowSmoothingPeriod, bool minSpreadEnabled, double minSpreadATRMultiplier, int minSpreadATRPeriod, bool showMarkers, int markerOffset, string uptrendMarker, string downtrendMarker)
		{
			return indicator.AAATrendSyncEquivalent(Input, fastPeriod, fastSmoothingEnabled, fastSmoothingPeriod, midPeriod, midSmoothingEnabled, midSmoothingPeriod, slowPeriod, slowSmoothingEnabled, slowSmoothingPeriod, minSpreadEnabled, minSpreadATRMultiplier, minSpreadATRPeriod, showMarkers, markerOffset, uptrendMarker, downtrendMarker);
		}

		public Indicators.AAATrendSyncEquivalent AAATrendSyncEquivalent(ISeries<double> input , int fastPeriod, bool fastSmoothingEnabled, int fastSmoothingPeriod, int midPeriod, bool midSmoothingEnabled, int midSmoothingPeriod, int slowPeriod, bool slowSmoothingEnabled, int slowSmoothingPeriod, bool minSpreadEnabled, double minSpreadATRMultiplier, int minSpreadATRPeriod, bool showMarkers, int markerOffset, string uptrendMarker, string downtrendMarker)
		{
			return indicator.AAATrendSyncEquivalent(input, fastPeriod, fastSmoothingEnabled, fastSmoothingPeriod, midPeriod, midSmoothingEnabled, midSmoothingPeriod, slowPeriod, slowSmoothingEnabled, slowSmoothingPeriod, minSpreadEnabled, minSpreadATRMultiplier, minSpreadATRPeriod, showMarkers, markerOffset, uptrendMarker, downtrendMarker);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AAATrendSyncEquivalent AAATrendSyncEquivalent(int fastPeriod, bool fastSmoothingEnabled, int fastSmoothingPeriod, int midPeriod, bool midSmoothingEnabled, int midSmoothingPeriod, int slowPeriod, bool slowSmoothingEnabled, int slowSmoothingPeriod, bool minSpreadEnabled, double minSpreadATRMultiplier, int minSpreadATRPeriod, bool showMarkers, int markerOffset, string uptrendMarker, string downtrendMarker)
		{
			return indicator.AAATrendSyncEquivalent(Input, fastPeriod, fastSmoothingEnabled, fastSmoothingPeriod, midPeriod, midSmoothingEnabled, midSmoothingPeriod, slowPeriod, slowSmoothingEnabled, slowSmoothingPeriod, minSpreadEnabled, minSpreadATRMultiplier, minSpreadATRPeriod, showMarkers, markerOffset, uptrendMarker, downtrendMarker);
		}

		public Indicators.AAATrendSyncEquivalent AAATrendSyncEquivalent(ISeries<double> input , int fastPeriod, bool fastSmoothingEnabled, int fastSmoothingPeriod, int midPeriod, bool midSmoothingEnabled, int midSmoothingPeriod, int slowPeriod, bool slowSmoothingEnabled, int slowSmoothingPeriod, bool minSpreadEnabled, double minSpreadATRMultiplier, int minSpreadATRPeriod, bool showMarkers, int markerOffset, string uptrendMarker, string downtrendMarker)
		{
			return indicator.AAATrendSyncEquivalent(input, fastPeriod, fastSmoothingEnabled, fastSmoothingPeriod, midPeriod, midSmoothingEnabled, midSmoothingPeriod, slowPeriod, slowSmoothingEnabled, slowSmoothingPeriod, minSpreadEnabled, minSpreadATRMultiplier, minSpreadATRPeriod, showMarkers, markerOffset, uptrendMarker, downtrendMarker);
		}
	}
}

#endregion
