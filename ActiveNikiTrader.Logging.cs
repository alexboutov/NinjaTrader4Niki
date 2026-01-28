#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class ActiveNikiTrader
    {
        #region Trade Tracking Fields
        // Entry tracking for detailed exit logging
        private string tradeEntryDirection = "";
        private double tradeEntryPrice = 0;
        private DateTime tradeEntryTime = DateTime.MinValue;
        private string lastExitReason = "";
        #endregion
        
        #region CSV Indicator Logging
        private void InitializeCSVLog()
        {
            if (!EnableIndicatorCSVLog) return;
            try
            {
                string dir = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "log");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                csvLogFilePath = System.IO.Path.Combine(dir, $"IndicatorValues_{DateTime.Now:yyyy-MM-dd}_{chartSessionId}.csv");
                csvWriter = new StreamWriter(csvLogFilePath, false) { AutoFlush = true };
                // Write CSV header - includes AAA_IsUp
                csvWriter.WriteLine("BarTime,Close,AIQ1_IsUp,RR_IsUp,DT_Signal,VY_IsUp,ET_IsUp,SW_IsUp,SW_Count,T3P_IsUp,AAA_IsUp,SB_IsUp,BullConf,BearConf,Source");
                LogAlways($"📊 CSV Log: {csvLogFilePath}");
            }
            catch (Exception ex) { Print($"CSV Init Error: {ex.Message}"); }
        }
        
        private void WriteCSVRow(DateTime barTime)
        {
            if (csvWriter == null || !EnableIndicatorCSVLog) return;
            try
            {
                var (bull, bear, total) = GetConfluence();
                string source = GetIndicatorSourceSummary();
                // Include AAA_IsUp in CSV output - barTime includes full date
                csvWriter.WriteLine($"{barTime:yyyy-MM-dd HH:mm:ss},{Close[0]:F2},{B2I(AIQ1_IsUp)},{B2I(RR_IsUp)},{DT_Signal:F2},{B2I(VY_IsUp)},{B2I(ET_IsUp)},{B2I(SW_IsUp)},{SW_Count},{B2I(T3P_IsUp)},{B2I(AAA_IsUp)},{B2I(SB_IsUp)},{bull},{bear},{source}");
            }
            catch { }
        }
        
        private int B2I(bool b) => b ? 1 : 0;
        
        private string GetIndicatorSourceSummary()
        {
            // Returns a short code indicating indicator sources: N=ninZa, C=Chart, H=Hosted, -=N/A
            string aiq = useNativeAiq1 ? "N" : (useChartAiq1 ? "C" : "H");
            string rr = rubyRiver != null ? "N" : (useChartRR ? "C" : "H");
            string dt = dragonTrend != null ? "N" : (useChartDT ? "C" : "H");
            string vy = vidyaPro != null ? "N" : (useChartVY ? "C" : "H");
            string et = easyTrend != null ? "N" : (useChartET ? "C" : "H");
            string sw = solarWave != null ? "N" : (useChartSW ? "C" : "H");
            string t3 = ninZaT3Pro != null ? "N" : (useChartT3P ? "C" : "H");
            string aaa = aaaTrendSync != null ? "N" : (useChartAAA ? "C" : "-");
            string sb = useNativeAiqSB ? "N" : (useChartSB ? "C" : "-");
            return $"AIQ:{aiq}|RR:{rr}|DT:{dt}|VY:{vy}|ET:{et}|SW:{sw}|T3:{t3}|AAA:{aaa}|SB:{sb}";
        }
        
        private void CloseCSVLog()
        {
            try { csvWriter?.Close(); } catch { }
        }
        #endregion
        
        #region Price Helpers
        private double GetCurrentAsk()
        {
            if (BarsInProgress == 0 && GetCurrentAsk(0) > 0)
                return GetCurrentAsk(0);
            return Close[0];
        }
        
        private double GetCurrentBid()
        {
            if (BarsInProgress == 0 && GetCurrentBid(0) > 0)
                return GetCurrentBid(0);
            return Close[0];
        }
        
        // Call this when entering a trade to track details for exit logging
        private void SetTradeEntry(string direction, double price, DateTime time)
        {
            tradeEntryDirection = direction;
            tradeEntryPrice = price;
            tradeEntryTime = time;
            lastExitReason = "";
        }
        
        // Call this to set exit reason before exit is executed
        private void SetExitReason(string reason)
        {
            lastExitReason = reason;
        }
        #endregion
        
        #region Signal Logging
        private void LogSignal(string dir, string trigger, DateTime t, int confluenceCount, int total)
        {
            double askPrice = GetCurrentAsk();
            double bidPrice = GetCurrentBid();
            double pointValue = Instrument.MasterInstrument.PointValue;
            double stopPoints = pointValue > 0 ? StopLossUSD / pointValue : 0;
            double tpPoints = pointValue > 0 ? TakeProfitUSD / pointValue : 0;
            
            double entryPriceLog, stopPrice, tpPrice;
            int barsAfterSquare;
            
            if (dir == "LONG")
            {
                entryPriceLog = askPrice;
                stopPrice = askPrice - stopPoints;
                tpPrice = askPrice + tpPoints;
                barsAfterSquare = barsSinceYellowSquare;
            }
            else
            {
                entryPriceLog = bidPrice;
                stopPrice = bidPrice + stopPoints;
                tpPrice = bidPrice - tpPoints;
                barsAfterSquare = barsSinceOrangeSquare;
            }
            
            string instrumentName = Instrument.FullName;
            string squareType = dir == "LONG" ? "Yellow🟨" : "Orange🟧";
            
            PrintAndLog($"");
            PrintAndLog($"╔══════════════════════════════════════════════════════════════════════════╗");
            // FIXED: Include full date in signal timestamp for Market Replay analysis
            PrintAndLog($"║  *** {dir} SIGNAL @ {t:yyyy-MM-dd HH:mm:ss} ***");
            PrintAndLog($"╠══════════════════════════════════════════════════════════════════════════╣");
            PrintAndLog($"║  Instrument: {instrumentName}");
            PrintAndLog($"║  Ask: {askPrice:F2}    Bid: {bidPrice:F2}");
            PrintAndLog($"║  STOP: {stopPrice:F2}  (${StopLossUSD:F0} = {stopPoints:F2} pts)");
            PrintAndLog($"║  TP:   {tpPrice:F2}  (${TakeProfitUSD:F0} = {tpPoints:F2} pts)");
            PrintAndLog($"╠══════════════════════════════════════════════════════════════════════════╣");
            PrintAndLog($"║  Trigger: {trigger}");
            PrintAndLog($"║  Confluence: {confluenceCount}/{total}");
            PrintAndLog($"║  RR={Ts(RR_IsUp)} DT={DT_Signal:F0} VY={Ts(VY_IsUp)} ET={Ts(ET_IsUp)} SW={SW_Count} T3P={Ts(T3P_IsUp)} AAA={Ts(AAA_IsUp)} SB={Ts(SB_IsUp)}");
            PrintAndLog($"║  AIQ1={Ts(AIQ1_IsUp)} | Bars after {squareType}: {barsAfterSquare}");
            PrintAndLog($"╚══════════════════════════════════════════════════════════════════════════╝");
        }
        
        private string Ts(bool up) => up ? "UP" : "DN";
        #endregion
        
        #region Logging
        private void InitializeLogFile()
        {
            try
            {
                string dir = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "log");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                logFilePath = System.IO.Path.Combine(dir, $"ActiveNikiTrader_{DateTime.Now:yyyy-MM-dd}_{chartSessionId}.txt");
                logWriter = new StreamWriter(logFilePath, true) { AutoFlush = true };
                logWriter.WriteLine($"\n=== ActiveNikiTrader Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                if (UniRenkoMode)
                {
                    logWriter.WriteLine($"    *** UNIRENKO MODE ***");
                    logWriter.WriteLine($"    Cooldown: {(UseTimeBasedCooldown ? $"{CooldownSeconds} seconds (time-based)" : $"{CooldownBars} bars")}");
                }
                logWriter.WriteLine($"    8-indicator confluence filter");
                logWriter.WriteLine($"    Signal Filter: MinConf={MinConfluenceRequired}/8, MaxBars={MaxBarsAfterYellowSquare}, Cooldown={CooldownBars}");
                logWriter.WriteLine($"    Auto Trade: {(EnableAutoTrading ? "ON" : "OFF")} | MinConf for Trade={MinConfluenceForAutoTrade}/8");
                logWriter.WriteLine($"    Risk: SL=${StopLossUSD:F0}, TP=${TakeProfitUSD:F0}");
                if (EnableDailyLossLimit)
                    logWriter.WriteLine($"    Daily Loss Limit: ${DailyLossLimitUSD:F0}");
                if (UseTradingHoursFilter)
                    logWriter.WriteLine($"    Trading Hours: {GetTradingHoursString()}");
                else
                    logWriter.WriteLine($"    Trading Hours: ALL (filter disabled)");
                if (CloseBeforeNews)
                    logWriter.WriteLine($"    Auto-Close Before News: {NewsCloseHour:D2}:{NewsCloseMinute:D2}");
                if (CloseAtEndOfDay)
                    logWriter.WriteLine($"    Auto-Close EOD: {EODCloseHour:D2}:{EODCloseMinute:D2}");
                logWriter.WriteLine($"    LONG:  Yellow🟨 (AIQ1 UP) → Any indicator confirms → Bull Confluence ≥ {MinConfluenceRequired}");
                logWriter.WriteLine($"    SHORT: Orange🟧 (AIQ1 DN) → Any indicator confirms → Bear Confluence ≥ {MinConfluenceRequired}\n");
            }
            catch { }
        }
        
        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order != null && execution.Order.OrderState == OrderState.Filled)
            {
                string orderName = execution.Order.Name ?? "";
                OrderAction action = execution.Order.OrderAction;
                
                // Detect ENTRY fills
                bool isEntry = (action == OrderAction.Buy || action == OrderAction.SellShort) && 
                               (orderName == "Long" || orderName == "Short" || orderName.Contains("Entry"));
                
                if (isEntry)
                {
                    string dir = (action == OrderAction.Buy) ? "LONG" : "SHORT";
                    
                    // Calculate entry slippage (positive = unfavorable)
                    double entrySlippageTicks = 0;
                    double entrySlippageDollars = 0;
                    if (signalPriceAtEntry > 0 && TickSize > 0)
                    {
                        if (dir == "LONG")
                            entrySlippageTicks = (price - signalPriceAtEntry) / TickSize;
                        else
                            entrySlippageTicks = (signalPriceAtEntry - price) / TickSize;
                        
                        entrySlippageDollars = entrySlippageTicks * TickSize * Instrument.MasterInstrument.PointValue;
                    }
                    
                    SetTradeEntry(dir, price, time);
                    
                    string slipStr = entrySlippageTicks >= 0 ? $"+{entrySlippageTicks:F0}t" : $"{entrySlippageTicks:F0}t";
                    string slipDollarStr = entrySlippageDollars >= 0 ? $"${entrySlippageDollars:F2}" : $"-${Math.Abs(entrySlippageDollars):F2}";
                    PrintAndLog($">>> ENTRY FILLED: {dir} @ {price:F2} | Signal={signalPriceAtEntry:F2} | Slippage: {slipStr} ({slipDollarStr}) | {time:yyyy-MM-dd HH:mm:ss}");
                }
                
                // Detect EXIT fills
                bool isExit = Position.MarketPosition == MarketPosition.Flat && 
                    (orderName.Contains("Stop") || orderName.Contains("Profit") || orderName.Contains("Exit") || 
                     action == OrderAction.Sell || action == OrderAction.BuyToCover);
                
                if (isExit && SystemPerformance.AllTrades.Count > 0)
                {
                    var lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                    double tradePnL = lastTrade.ProfitCurrency;
                    double exitPrice = price;
                    
                    // Determine exit reason from order name
                    string exitReason = "UNKNOWN";
                    if (!string.IsNullOrEmpty(lastExitReason))
                        exitReason = lastExitReason;
                    else if (orderName.Contains("Stop"))
                        exitReason = simpleTrailActive ? "TRAIL" : "SL";
                    else if (orderName.Contains("Profit"))
                        exitReason = "TP";
                    else if (orderName.Contains("Exit"))
                        exitReason = orderName.Replace(" ", "_");
                    else if (orderName.Contains("MaxProfit"))
                        exitReason = "MAX_PROFIT";
                    
                    // Calculate ticks P&L
                    double ticksPnL = 0;
                    if (tradeEntryPrice > 0 && TickSize > 0)
                    {
                        if (tradeEntryDirection == "LONG")
                            ticksPnL = (exitPrice - tradeEntryPrice) / TickSize;
                        else if (tradeEntryDirection == "SHORT")
                            ticksPnL = (tradeEntryPrice - exitPrice) / TickSize;
                    }
                    
                    // Calculate exit slippage (positive = unfavorable)
                    double exitSlippageTicks = 0;
                    if (TickSize > 0)
                    {
                        if (exitReason == "TP" && expectedTargetPrice > 0)
                        {
                            // TP is a limit order - should have 0 or favorable slippage
                            if (tradeEntryDirection == "LONG")
                                exitSlippageTicks = (expectedTargetPrice - exitPrice) / TickSize;
                            else
                                exitSlippageTicks = (exitPrice - expectedTargetPrice) / TickSize;
                        }
                        else if ((exitReason == "SL" || exitReason == "TRAIL") && expectedStopPrice > 0)
                        {
                            // Stop orders can have slippage
                            if (tradeEntryDirection == "LONG")
                                exitSlippageTicks = (expectedStopPrice - exitPrice) / TickSize;
                            else
                                exitSlippageTicks = (exitPrice - expectedStopPrice) / TickSize;
                        }
                    }
                    
                    dailyPnL += tradePnL;
                    dailyTradeCount++;
                    
                    string pnlIcon = tradePnL >= 0 ? "✅" : "❌";
                    string ticksStr = ticksPnL >= 0 ? $"+{ticksPnL:F0}t" : $"{ticksPnL:F0}t";
                    string exitSlipStr = exitSlippageTicks >= 0 ? $"+{exitSlippageTicks:F0}t" : $"{exitSlippageTicks:F0}t";
                    
                    // Comprehensive trade closed log line for analysis script - includes exit slippage
                    PrintAndLog($"{pnlIcon} TRADE CLOSED: {tradeEntryDirection} | Entry={tradeEntryPrice:F2} Exit={exitPrice:F2} | {ticksStr} ${tradePnL:F2} | Reason: {exitReason} | Exit Slip: {exitSlipStr}");
                    PrintAndLog($"   Daily P&L: ${dailyPnL:F2} ({dailyTradeCount} trades) | Entry Time: {tradeEntryTime:yyyy-MM-dd HH:mm:ss}");
                    
                    // Reset entry tracking
                    tradeEntryDirection = "";
                    tradeEntryPrice = 0;
                    tradeEntryTime = DateTime.MinValue;
                    lastExitReason = "";
                    
                    if (EnableSoundAlert)
                        try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                    
                    if (EnableDailyLossLimit && dailyPnL <= -DailyLossLimitUSD)
                    {
                        dailyLossLimitHit = true;
                        PrintAndLog($"🛑 DAILY LOSS LIMIT HIT: ${dailyPnL:F2} exceeds -${DailyLossLimitUSD:F2} limit. Trading stopped for today.");
                        if (EnableSoundAlert)
                            try { System.Media.SystemSounds.Hand.Play(); } catch { }
                    }
                    
                    if (EnableDailyProfitTarget && dailyPnL >= DailyProfitTargetUSD)
                    {
                        dailyProfitTargetHit = true;
                        PrintAndLog($"🎯 DAILY PROFIT TARGET HIT: ${dailyPnL:F2} reached ${DailyProfitTargetUSD:F2} target. Trading stopped for today.");
                        if (EnableSoundAlert)
                            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                    }
                }
            }
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
        
        private void PrintAndLog(string msg)
        {
            Print(msg);
            if (logWriter != null)
                try { logWriter.WriteLine($"{DateTime.Now:HH:mm:ss} | {msg}"); } catch { }
        }
        #endregion
    }
}
