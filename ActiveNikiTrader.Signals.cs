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
        
        private int GetEnabledCount() => (UseRubyRiver?1:0)+(UseDragonTrend?1:0)+(UseSolarWave?1:0)+(UseVIDYAPro?1:0)+(UseEasyTrend?1:0)+(UseT3Pro?1:0)+(UseAAATrendSync && AAA_Available?1:0)+(UseAIQSuperBands && SB_Available?1:0);
        
        private (int bull, int bear, int total) GetConfluence()
        {
            int bull = 0, bear = 0, total = 0;
            if (UseRubyRiver) { total++; if (RR_IsUp) bull++; else bear++; }
            if (UseDragonTrend) { total++; if (DT_IsUp) bull++; else if (DT_IsDown) bear++; }
            if (UseSolarWave) { total++; if (SW_IsUp && SW_Count >= MinSolarWaveCount) bull++; else if (!SW_IsUp && SW_Count <= -MinSolarWaveCount) bear++; }
            if (UseVIDYAPro) { total++; if (VY_IsUp) bull++; else bear++; }
            if (UseEasyTrend) { total++; if (ET_IsUp) bull++; else bear++; }
            if (UseT3Pro) { total++; if (T3P_IsUp) bull++; else bear++; }
            if (UseAAATrendSync && AAA_Available) { total++; if (AAA_IsUp) bull++; else bear++; }
            if (UseAIQSuperBands && SB_Available) { total++; if (SB_IsUp) bull++; else bear++; }
            return (bull, bear, total);
        }
        
        private string GetBullishConfirmation()
        {
            if (isFirstBar) return null;
            
            // Check each enabled indicator for a bullish flip (was DOWN, now UP)
            if (UseRubyRiver && RR_IsUp && !prevRR_IsUp) return "RR";
            if (UseDragonTrend && DT_IsUp && !prevDT_IsUp) return "DT";
            if (UseVIDYAPro && VY_IsUp && !prevVY_IsUp) return "VY";
            if (UseEasyTrend && ET_IsUp && !prevET_IsUp) return "ET";
            if (UseSolarWave && SW_IsUp && !prevSW_IsUp) return "SW";
            if (UseT3Pro && T3P_IsUp && !prevT3P_IsUp) return "T3P";
            if (UseAAATrendSync && AAA_Available && AAA_IsUp && !prevAAA_IsUp) return "AAA";
            if (UseAIQSuperBands && SB_Available && SB_IsUp && !prevSB_IsUp) return "SB";
            
            // Also check if indicator is already UP (in case it flipped same bar as AIQ1)
            if (UseRubyRiver && RR_IsUp) return "RR";
            if (UseDragonTrend && DT_IsUp) return "DT";
            if (UseVIDYAPro && VY_IsUp) return "VY";
            if (UseEasyTrend && ET_IsUp) return "ET";
            if (UseSolarWave && SW_IsUp) return "SW";
            if (UseT3Pro && T3P_IsUp) return "T3P";
            if (UseAAATrendSync && AAA_Available && AAA_IsUp) return "AAA";
            if (UseAIQSuperBands && SB_Available && SB_IsUp) return "SB";
            
            return null;
        }
        
        private string GetBearishConfirmation()
        {
            if (isFirstBar) return null;
            
            // Check each enabled indicator for a bearish flip (was UP, now DOWN)
            if (UseRubyRiver && !RR_IsUp && prevRR_IsUp) return "RR";
            if (UseDragonTrend && DT_IsDown && !prevDT_IsUp) return "DT";
            if (UseVIDYAPro && !VY_IsUp && prevVY_IsUp) return "VY";
            if (UseEasyTrend && !ET_IsUp && prevET_IsUp) return "ET";
            if (UseSolarWave && !SW_IsUp && prevSW_IsUp) return "SW";
            if (UseT3Pro && !T3P_IsUp && prevT3P_IsUp) return "T3P";
            if (UseAAATrendSync && AAA_Available && !AAA_IsUp && prevAAA_IsUp) return "AAA";
            if (UseAIQSuperBands && SB_Available && !SB_IsUp && prevSB_IsUp) return "SB";
            
            // Also check if indicator is already DOWN (in case it flipped same bar as AIQ1)
            if (UseRubyRiver && !RR_IsUp) return "RR";
            if (UseDragonTrend && DT_IsDown) return "DT";
            if (UseVIDYAPro && !VY_IsUp) return "VY";
            if (UseEasyTrend && !ET_IsUp) return "ET";
            if (UseSolarWave && !SW_IsUp) return "SW";
            if (UseT3Pro && !T3P_IsUp) return "T3P";
            if (UseAAATrendSync && AAA_Available && !AAA_IsUp) return "AAA";
            if (UseAIQSuperBands && SB_Available && !SB_IsUp) return "SB";
            
            return null;
        }
        
        private bool IsTradingHoursAllowed(DateTime barTime)
        {
            if (!UseTradingHoursFilter) return true;
            
            int currentMinutes = barTime.Hour * 60 + barTime.Minute;
            int session1Start = Session1StartHour * 60 + Session1StartMinute;
            int session1End = Session1EndHour * 60 + Session1EndMinute;
            int session2Start = Session2StartHour * 60 + Session2StartMinute;
            int session2End = Session2EndHour * 60 + Session2EndMinute;
            
            bool inSession1 = currentMinutes >= session1Start && currentMinutes <= session1End;
            bool inSession2 = currentMinutes >= session2Start && currentMinutes <= session2End;
            
            return inSession1 || inSession2;
        }
        
        private string GetTradingHoursString()
        {
            return $"{Session1StartHour:D2}:{Session1StartMinute:D2}-{Session1EndHour:D2}:{Session1EndMinute:D2}, {Session2StartHour:D2}:{Session2StartMinute:D2}-{Session2EndHour:D2}:{Session2EndMinute:D2}";
        }

        #endregion
    }
}
