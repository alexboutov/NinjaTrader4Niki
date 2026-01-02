#!/usr/bin/env python3
# Patch script to add Daily Profit Target feature to ActiveNikiTrader.cs
# Usage: python add_daily_profit_target.py <path_to_file>

import sys
import os
import shutil
from datetime import datetime

def patch_file(filepath):
    # Create backup
    backup_path = filepath + ".backup_" + datetime.now().strftime('%Y%m%d_%H%M%S')
    shutil.copy2(filepath, backup_path)
    print("Backup created: " + backup_path)
    
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    original_content = content
    changes_made = 0
    
    # CHANGE 1: Add variable
    old1 = "private bool dailyLossLimitHit = false;\n        private int dailyTradeCount = 0;"
    new1 = "private bool dailyLossLimitHit = false;\n        private bool dailyProfitTargetHit = false;\n        private int dailyTradeCount = 0;"
    if old1 in content:
        content = content.replace(old1, new1)
        changes_made += 1
        print("[OK] Change 1: Added dailyProfitTargetHit variable")
    else:
        print("[FAIL] Change 1: Pattern not found (variable)")
    
    # CHANGE 2: Add parameters
    old2 = '''[NinjaScriptProperty]
        [Display(Name="Reset Daily P&L at Session Start", Description="Reset daily P&L tracking at start of trading session", Order=3, GroupName="11. Risk Management")]
        public bool ResetDailyPnLAtSessionStart { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name="Stop Loss Buffer Ticks", Description="Extra ticks added to stop loss to reduce slippage (0=disabled)", Order=4, GroupName="11. Risk Management")]
        public int StopLossBufferTicks { get; set; }'''
    
    new2 = '''[NinjaScriptProperty]
        [Display(Name="Reset Daily P&L at Session Start", Description="Reset daily P&L tracking at start of trading session", Order=3, GroupName="11. Risk Management")]
        public bool ResetDailyPnLAtSessionStart { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Enable Daily Profit Target", Description="Stop trading if daily profit exceeds target", Order=4, GroupName="11. Risk Management")]
        public bool EnableDailyProfitTarget { get; set; }
        
        [NinjaScriptProperty]
        [Range(100, 10000)]
        [Display(Name="Daily Profit Target USD", Description="Stop trading after reaching this profit", Order=5, GroupName="11. Risk Management")]
        public double DailyProfitTargetUSD { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name="Stop Loss Buffer Ticks", Description="Extra ticks added to stop loss to reduce slippage (0=disabled)", Order=6, GroupName="11. Risk Management")]
        public int StopLossBufferTicks { get; set; }'''
    
    if old2 in content:
        content = content.replace(old2, new2)
        changes_made += 1
        print("[OK] Change 2: Added EnableDailyProfitTarget and DailyProfitTargetUSD parameters")
    else:
        print("[FAIL] Change 2: Pattern not found (parameters)")
    
    # CHANGE 3: Add default values
    old3 = '''// Risk Management
                EnableDailyLossLimit = true;
                DailyLossLimitUSD = 300;
                ResetDailyPnLAtSessionStart = true;
                StopLossBufferTicks = 2;'''
    
    new3 = '''// Risk Management
                EnableDailyLossLimit = true;
                DailyLossLimitUSD = 300;
                EnableDailyProfitTarget = true;
                DailyProfitTargetUSD = 600;
                ResetDailyPnLAtSessionStart = true;
                StopLossBufferTicks = 2;'''
    
    if old3 in content:
        content = content.replace(old3, new3)
        changes_made += 1
        print("[OK] Change 3: Added default values (EnableDailyProfitTarget=true, DailyProfitTargetUSD=600)")
    else:
        print("[FAIL] Change 3: Pattern not found (defaults)")
    
    # CHANGE 4: Add startup log message
    old4 = 'if (EnableDailyLossLimit)\n                    LogAlways($"🛡️ Daily Loss Limit: ${DailyLossLimitUSD:F0}");\n                if (EnableIndicatorCSVLog)'
    
    new4 = 'if (EnableDailyLossLimit)\n                    LogAlways($"🛡️ Daily Loss Limit: ${DailyLossLimitUSD:F0}");\n                if (EnableDailyProfitTarget)\n                    LogAlways($"🎯 Daily Profit Target: ${DailyProfitTargetUSD:F0}");\n                if (EnableIndicatorCSVLog)'
    
    if old4 in content:
        content = content.replace(old4, new4)
        changes_made += 1
        print("[OK] Change 4: Added startup log message for profit target")
    else:
        print("[FAIL] Change 4: Pattern not found (startup log)")
    
    # CHANGE 5: Update daily reset logic
    old5 = '''dailyPnL = 0;
                dailyTradeCount = 0;
                dailyLossLimitHit = false;
                lastTradeDate = barTime.Date;'''
    
    new5 = '''dailyPnL = 0;
                dailyTradeCount = 0;
                dailyLossLimitHit = false;
                dailyProfitTargetHit = false;
                lastTradeDate = barTime.Date;'''
    
    if old5 in content:
        content = content.replace(old5, new5)
        changes_made += 1
        print("[OK] Change 5: Added dailyProfitTargetHit reset on new day")
    else:
        print("[FAIL] Change 5: Pattern not found (daily reset)")
    
    # CHANGE 6: Update daily limit check
    old6 = '''// Daily loss limit check
            if (EnableDailyLossLimit && dailyLossLimitHit)
            {
                // Already hit limit - no more trading today
                return;
            }'''
    
    new6 = '''// Daily loss limit / profit target check
            if ((EnableDailyLossLimit && dailyLossLimitHit) || (EnableDailyProfitTarget && dailyProfitTargetHit))
            {
                // Already hit limit or target - no more trading today
                return;
            }'''
    
    if old6 in content:
        content = content.replace(old6, new6)
        changes_made += 1
        print("[OK] Change 6: Updated daily limit check to include profit target")
    else:
        print("[FAIL] Change 6: Pattern not found (daily limit check)")
    
    # CHANGE 7: Add profit target check in OnExecutionUpdate
    old7 = '''// Check if daily loss limit hit
                    if (EnableDailyLossLimit && dailyPnL <= -DailyLossLimitUSD)
                    {
                        dailyLossLimitHit = true;
                        PrintAndLog($"🛑 DAILY LOSS LIMIT HIT: ${dailyPnL:F2} exceeds -${DailyLossLimitUSD:F2} limit. Trading stopped for today.");
                        if (EnableSoundAlert)
                            try { System.Media.SystemSounds.Hand.Play(); } catch { }
                    }
                }
            }
        }'''
    
    new7 = '''// Check if daily loss limit hit
                    if (EnableDailyLossLimit && dailyPnL <= -DailyLossLimitUSD)
                    {
                        dailyLossLimitHit = true;
                        PrintAndLog($"🛑 DAILY LOSS LIMIT HIT: ${dailyPnL:F2} exceeds -${DailyLossLimitUSD:F2} limit. Trading stopped for today.");
                        if (EnableSoundAlert)
                            try { System.Media.SystemSounds.Hand.Play(); } catch { }
                    }
                    
                    // Check if daily profit target hit
                    if (EnableDailyProfitTarget && dailyPnL >= DailyProfitTargetUSD)
                    {
                        dailyProfitTargetHit = true;
                        PrintAndLog($"🎯 DAILY PROFIT TARGET HIT: ${dailyPnL:F2} reached ${DailyProfitTargetUSD:F2} target. Trading stopped for today.");
                        if (EnableSoundAlert)
                            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                    }
                }
            }
        }'''
    
    if old7 in content:
        content = content.replace(old7, new7)
        changes_made += 1
        print("[OK] Change 7: Added profit target check in OnExecutionUpdate")
    else:
        print("[FAIL] Change 7: Pattern not found (OnExecutionUpdate)")
    
    # CHANGE 8: Update panel display
    old8 = 'string dailyPnLText = EnableDailyLossLimit ? $" | Day: ${dailyPnL:F0}" : "";'
    new8 = 'string dailyPnLText = (EnableDailyLossLimit || EnableDailyProfitTarget) ? $" | Day: ${dailyPnL:F0}" : "";'
    
    if old8 in content:
        content = content.replace(old8, new8)
        changes_made += 1
        print("[OK] Change 8a: Updated panel dailyPnLText")
    else:
        print("[FAIL] Change 8a: Pattern not found (panel dailyPnLText)")
    
    old9 = 'string limitHitText = dailyLossLimitHit ? " 🛑STOPPED" : "";'
    new9 = 'string limitHitText = dailyLossLimitHit ? " 🛑STOPPED" : (dailyProfitTargetHit ? " 🎯TARGET" : "");'
    
    if old9 in content:
        content = content.replace(old9, new9)
        changes_made += 1
        print("[OK] Change 8b: Updated panel limitHitText")
    else:
        print("[FAIL] Change 8b: Pattern not found (panel limitHitText)")
    
    # Write changes
    if changes_made > 0:
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
        print("")
        print("=" * 60)
        print("DONE! %d changes applied to %s" % (changes_made, filepath))
        print("=" * 60)
        print("")
        print("New parameters added:")
        print("  - Enable Daily Profit Target: True")
        print("  - Daily Profit Target USD: $600")
        print("")
        print("Next steps:")
        print("  1. Open NinjaTrader")
        print("  2. Go to Tools > NinjaScript Editor")
        print("  3. Press F5 to compile")
        print("  4. Verify no compilation errors")
        return True
    else:
        print("")
        print("[FAIL] No changes made - patterns not found")
        print("The file may have been modified or already patched")
        return False

if __name__ == "__main__":
    print("=" * 60)
    print("Daily Profit Target Patch for ActiveNikiTrader")
    print("=" * 60)
    
    if len(sys.argv) > 1:
        filepath = sys.argv[1]
    else:
        print("Usage: python add_daily_profit_target.py <path_to_file>")
        print("Example: python add_daily_profit_target.py ActiveNikiTrader.cs")
        sys.exit(1)
    
    print("Target file: " + filepath)
    print("")
    
    if not os.path.exists(filepath):
        print("[FAIL] File not found: " + filepath)
        sys.exit(1)
    
    patch_file(filepath)
