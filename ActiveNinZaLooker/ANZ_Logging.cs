using System;
using System.IO;
namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class ActiveNinZaLooker : Indicator
    {
        #region Logging

        private void InitializeLogFile()
        {
            try
            {
                string dir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "log");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                logFilePath = Path.Combine(dir, $"ActiveNinZaLooker_{DateTime.Now:yyyy-MM-dd}_{chartSessionId}.txt");
                logWriter   = new StreamWriter(logFilePath, true) { AutoFlush = true };
                logWriter.WriteLine($"\n=== ActiveNinZaLooker Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                logWriter.WriteLine($"    MinConfluenceToTrade={MinConfluenceToTrade}  MaxBarsAfterSquare={MaxBarsAfterSquare}  CooldownBars={CooldownBars}");
                logWriter.WriteLine($"    Mode: MONITOR ONLY — no orders placed\n");
            }
            catch { }
        }

        private void CloseLogFile()
        {
            try
            {
                logWriter?.WriteLine($"\n=== Session Ended: {DateTime.Now:HH:mm:ss} | Signals: {signalCount} ===");
                logWriter?.Close();
            }
            catch { }
        }

        private void Log(string msg)
        {
            Print(msg);
            if (logWriter == null) return;
            try
            {
                string ts = CurrentBar >= 0
                    ? Time[0].ToString("yyyy-MM-dd HH:mm:ss")
                    : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                logWriter.WriteLine($"{ts} | {msg}");
            }
            catch { }
        }

        private string U(bool up)       => up ? "UP" : "DN";
        private string SrcLabel(bool hasNinZa, bool hasChart)
            => hasNinZa ? "ninZa" : hasChart ? "CHART" : "HOSTED";

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
