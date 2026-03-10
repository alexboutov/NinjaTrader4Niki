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
        #region Signal and Confluence Logic
        
        private int GetEnabledCount() =>
            (UseRubyRiver    ? 1 : 0) +
            (UseDragonTrend  ? 1 : 0) +
            (UseSolarWave    ? 1 : 0) +
            (UseVIDYAPro     ? 1 : 0) +
            (UseEasyTrend    ? 1 : 0) +
            (UseT3Pro        ? 1 : 0) +
            (UseAAATrendSync && AAA_Available ? 1 : 0) +
            (UseAIQSuperBands && SB_Available ? 1 : 0);
        
        private (int bull, int bear, int total) GetConfluence()
        {
            int bull = 0, bear = 0, total = 0;
            if (UseRubyRiver)    { total++; if (RR_IsUp) bull++; else bear++; }
            if (UseDragonTrend)  { total++; if (DT_IsUp) bull++; else if (DT_IsDown) bear++; }
            if (UseSolarWave)    { total++; if (SW_IsUp && SW_Count >= MinSolarWaveCount) bull++; else if (!SW_IsUp && SW_Count <= -MinSolarWaveCount) bear++; }
            if (UseVIDYAPro)     { total++; if (VY_IsUp) bull++; else bear++; }
            if (UseEasyTrend)    { total++; if (ET_IsUp) bull++; else bear++; }
            if (UseT3Pro)        { total++; if (T3P_IsUp) bull++; else bear++; }
            if (UseAAATrendSync  && AAA_Available) { total++; if (AAA_IsUp) bull++; else bear++; }
            if (UseAIQSuperBands && SB_Available)  { total++; if (SB_IsUp)  bull++; else bear++; }
            return (bull, bear, total);
        }
        
        private string GetBullishConfirmation()
        {
            if (isFirstBar) return null;
            
            // Priority 1: fresh flips (was DOWN, now UP) this bar
            if (UseRubyRiver    && RR_IsUp  && !prevRR_IsUp)  return "RR";
            if (UseDragonTrend  && DT_IsUp  && !prevDT_IsUp)  return "DT";
            if (UseVIDYAPro     && VY_IsUp  && !prevVY_IsUp)  return "VY";
            if (UseEasyTrend    && ET_IsUp  && !prevET_IsUp)  return "ET";
            if (UseSolarWave    && SW_IsUp  && !prevSW_IsUp)  return "SW";
            if (UseT3Pro        && T3P_IsUp && !prevT3P_IsUp) return "T3P";
            if (UseAAATrendSync && AAA_Available && AAA_IsUp  && !prevAAA_IsUp) return "AAA";
            if (UseAIQSuperBands && SB_Available && SB_IsUp   && !prevSB_IsUp)  return "SB";
            
            // Priority 2: already UP (flipped same bar as AIQ1 trigger)
            if (UseRubyRiver    && RR_IsUp)  return "RR";
            if (UseDragonTrend  && DT_IsUp)  return "DT";
            if (UseVIDYAPro     && VY_IsUp)  return "VY";
            if (UseEasyTrend    && ET_IsUp)  return "ET";
            if (UseSolarWave    && SW_IsUp)  return "SW";
            if (UseT3Pro        && T3P_IsUp) return "T3P";
            if (UseAAATrendSync && AAA_Available && AAA_IsUp) return "AAA";
            if (UseAIQSuperBands && SB_Available && SB_IsUp)  return "SB";
            
            return null;
        }
        
        private string GetBearishConfirmation()
        {
            if (isFirstBar) return null;
            
            // Priority 1: fresh flips (was UP, now DOWN) this bar
            if (UseRubyRiver    && !RR_IsUp  && prevRR_IsUp)  return "RR";
            if (UseDragonTrend  && DT_IsDown && !prevDT_IsUp) return "DT";
            if (UseVIDYAPro     && !VY_IsUp  && prevVY_IsUp)  return "VY";
            if (UseEasyTrend    && !ET_IsUp  && prevET_IsUp)  return "ET";
            if (UseSolarWave    && !SW_IsUp  && prevSW_IsUp)  return "SW";
            if (UseT3Pro        && !T3P_IsUp && prevT3P_IsUp) return "T3P";
            if (UseAAATrendSync && AAA_Available && !AAA_IsUp && prevAAA_IsUp) return "AAA";
            if (UseAIQSuperBands && SB_Available && !SB_IsUp  && prevSB_IsUp)  return "SB";
            
            // Priority 2: already DOWN (flipped same bar as AIQ1 trigger)
            if (UseRubyRiver    && !RR_IsUp)  return "RR";
            if (UseDragonTrend  && DT_IsDown) return "DT";
            if (UseVIDYAPro     && !VY_IsUp)  return "VY";
            if (UseEasyTrend    && !ET_IsUp)  return "ET";
            if (UseSolarWave    && !SW_IsUp)  return "SW";
            if (UseT3Pro        && !T3P_IsUp) return "T3P";
            if (UseAAATrendSync && AAA_Available && !AAA_IsUp) return "AAA";
            if (UseAIQSuperBands && SB_Available && !SB_IsUp)  return "SB";
            
            return null;
        }
        
        private bool IsTradingHoursAllowed(DateTime barTime)
        {
            if (!UseTradingHoursFilter) return true;
            
            int currentMinutes = barTime.Hour * 60 + barTime.Minute;
            int session1Start  = Session1StartHour * 60 + Session1StartMinute;
            int session1End    = Session1EndHour   * 60 + Session1EndMinute;
            int session2Start  = Session2StartHour * 60 + Session2StartMinute;
            int session2End    = Session2EndHour   * 60 + Session2EndMinute;
            
            bool inSession1 = currentMinutes >= session1Start && currentMinutes <= session1End;
            bool inSession2 = currentMinutes >= session2Start && currentMinutes <= session2End;
            
            return inSession1 || inSession2;
        }
        
        private string GetTradingHoursString()
        {
            return $"{Session1StartHour:D2}:{Session1StartMinute:D2}-{Session1EndHour:D2}:{Session1EndMinute:D2}" +
                   $", {Session2StartHour:D2}:{Session2StartMinute:D2}-{Session2EndHour:D2}:{Session2EndMinute:D2}";
        }

        #endregion

        #region Force Close Logic

        /// <summary>
        /// Throttle field: tracks the last minute in which a force-close was already
        /// attempted, preventing log spam on every tick after EOD threshold is crossed.
        /// Reset to -1 at the start of each new trading day.
        /// </summary>
        private int lastForceCloseMinute = -1;

        /// <summary>
        /// Evaluates whether a news-window or EOD force-close should fire right now.
        /// Called from both OnBarUpdate (bar-level) and OnMarketData (tick-level sentinel).
        /// The per-minute throttle ensures only one close attempt per clock minute
        /// regardless of how frequently this is called.
        /// </summary>
        private void CheckForceClose(DateTime t)
        {
            if (Position.MarketPosition == MarketPosition.Flat) return;

            int currentMinutes = t.Hour * 60 + t.Minute;

            // --- News close (highest priority) ---
            // Fires during the 32-minute window starting at NewsCloseHour:NewsCloseMinute
            if (CloseBeforeNews)
            {
                int newsClose = NewsCloseHour * 60 + NewsCloseMinute;
                if (currentMinutes >= newsClose && currentMinutes < newsClose + 32)
                {
                    if (lastForceCloseMinute != currentMinutes)
                    {
                        lastForceCloseMinute = currentMinutes;
                        ForceClosePosition(t, "PreNews Exit");
                    }
                    return;  // Don't fall through to EOD check during news window
                }
            }

            // --- EOD hard close ---
            // Fires at or after EODCloseHour:EODCloseMinute, once per minute
            if (CloseAtEndOfDay)
            {
                int eodClose = EODCloseHour * 60 + EODCloseMinute;
                if (currentMinutes >= eodClose)
                {
                    if (lastForceCloseMinute != currentMinutes)
                    {
                        lastForceCloseMinute = currentMinutes;
                        ForceClosePosition(t, "EOD Exit");
                    }
                }
            }
        }

        /// <summary>
        /// Issues the actual exit order and logs it.
        /// Safe to call redundantly — guards on MarketPosition.Flat.
        /// </summary>
        private void ForceClosePosition(DateTime t, string reason)
        {
            if (Position.MarketPosition == MarketPosition.Flat) return;

            SetExitReason(reason);

            if (Position.MarketPosition == MarketPosition.Long)
            {
                ExitLong("Long", reason);
                PrintAndLog($">>> AUTO-CLOSE LONG @ {t:HH:mm:ss} - {reason}");
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                ExitShort("Short", reason);
                PrintAndLog($">>> AUTO-CLOSE SHORT @ {t:HH:mm:ss} - {reason}");
            }
        }

        #endregion

        #region OnMarketData Tick Sentinel

        /// <summary>
        /// Tick-level EOD / news force-close sentinel.
        ///
        /// Problem it solves: OnBarUpdate only fires when a new bar closes.
        /// On UniRenko bars during slow market periods, a bar may not form for
        /// many minutes — meaning the EOD close logic in OnBarUpdate would never
        /// run, leaving the position open overnight.
        ///
        /// This override fires on every Last-price tick during Realtime state,
        /// guaranteeing CheckForceClose is evaluated on real wall-clock time
        /// regardless of bar activity.
        ///
        /// Guards:
        ///   - State == Realtime: skips historical playback (avoids thousands of
        ///     spurious calls during Market Replay warm-up)
        ///   - MarketDataType.Last: only real trade prints, not bid/ask updates
        ///   - MarketPosition.Flat: exits immediately if already flat
        /// </summary>
        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (State != State.Realtime)                          return;
            if (e.MarketDataType != MarketDataType.Last)          return;
            if (Position.MarketPosition == MarketPosition.Flat)   return;

            CheckForceClose(e.Time);
        }

        #endregion
    }
}
