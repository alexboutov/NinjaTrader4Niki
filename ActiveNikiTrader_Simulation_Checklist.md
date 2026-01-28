# ActiveNikiTrader Simulation Setup Checklist (Updated)

**Last Updated:** January 28, 2026  
**NinjaTrader Version:** 8.1.6.3 64-bit  
**Author:** Alex (with Claude assistance)

---

## Purpose
Run ActiveNikiTrader strategy on Simulation connection (Sim102_Data_Collect) without interfering with partner's discretionary trading on Sim101.

---

## Account Configuration

| User | Account | Connection | Purpose |
|------|---------|------------|---------|
| Partner | Sim101 | Simulation | Discretionary manual trading |
| Alex | Sim102_Data_Collect | Simulation | ActiveNikiTrader automated strategy |

**Note:** Both accounts use the Simulation connection but are completely isolated — trades on one account do not affect the other.

---

## Pre-Setup: Create Second Simulation Account (If Needed)

### Enable Multi-provider Mode
1. [ ] Tools → **Settings** → General
2. [ ] Verify **Multi-provider** is checked (✓)
3. [ ] If changed, restart NinjaTrader

### Add Simulation Account
1. [ ] Control Center → **Accounts** tab
2. [ ] Right-click in blank area → **Add Simulation Account**
3. [ ] Configure:
   - Name: `Sim102_Data_Collect`
   - Initial cash: `$8,000` (or preferred amount)
4. [ ] Click OK
5. [ ] Verify new account appears in Accounts tab

---

## Pre-Setup Verification

- [ ] Partner's trading charts use **Sim101** account
- [ ] Partner is using **ActiveNikiMonitor** (indicator), NOT ActiveNikiTrader (strategy)
- [ ] Sim102_Data_Collect account is visible in Accounts tab
- [ ] Simulation connection shows green dot (connected)

---

## Step 1: Connect to Simulation

1. [ ] Control Center → Connections → **Simulation** → Connect
2. [ ] Verify green dot appears next to Simulation
3. [ ] Verify both accounts appear in Accounts tab:
   - [ ] Sim101
   - [ ] Sim102_Data_Collect

---

## Step 2: Create Dedicated Chart for Strategy

1. [ ] Control Center → New → **Chart**
2. [ ] Select instrument: **NQ MAR26** (or current front month)
3. [ ] Set timeframe: **10 UniRenko T10R50O27**
4. [ ] Verify chart is on **Simulation** connection
5. [ ] Set Chart Trader account to **Sim102_Data_Collect**

---

## Step 3: Add Required Indicators to Chart

Add these indicators (if not already present):
- [ ] AIQ_1
- [ ] AIQ_SuperBands
- [ ] ninZaRubyRiver
- [ ] ninZaDragonTrend
- [ ] ninZaVIDYAPro
- [ ] ninZaEasyTrend
- [ ] ninZaSolarWave
- [ ] ninZaAAATrendSync
- [ ] T3ProEquivalent (substitute for ninZaT3Pro)
- [ ] BltTriggerLines

---

## Step 4: Add ActiveNikiTrader Strategy

1. [ ] Right-click chart → Strategies → **Add Strategy**
2. [ ] Select **ActiveNikiTrader**

### ⚠️ CRITICAL: Account Selection
The **Account** field in the strategy configuration is SEPARATE from the Chart Trader dropdown. You must set it explicitly:

3. [ ] In the strategy configuration dialog, find **Account** field
4. [ ] Change from Sim101 to **Sim102_Data_Collect**

### Strategy Parameters

| Parameter | Value | Verified |
|-----------|-------|----------|
| **Account** | Sim102_Data_Collect | ⚠️ CRITICAL |
| Enabled | False (initially) | [ ] |
| EnableAutoTrading | True | [ ] |
| Session 1 Start Hour | 10 | [ ] |
| Session 1 Start Minute | 0 | [ ] |
| Session 1 End Hour | 10 | [ ] |
| Session 1 End Minute | 59 | [ ] |
| StopLossUSD | 200 (10 points) | [ ] |
| TakeProfitUSD | 600 (30 points) | [ ] |
| EnableTrailingStop | True | [ ] |
| TrailingStopActivation | 80 ticks | [ ] |
| TrailingStopDistance | 40 ticks | [ ] |

5. [ ] Click **OK** (strategy remains disabled)

---

## Step 5: Pre-Enable Safety Check

### ⚠️ CRITICAL: Verify Account Assignment
This is the most important check. The Chart Trader account and Strategy account are DIFFERENT settings.

1. [ ] Go to Control Center → **Strategies** tab
2. [ ] Find ActiveNikiTrader in the list
3. [ ] Verify **Account display name** column shows **Sim102_Data_Collect**
   - If it shows Sim101 → STOP → Remove strategy and re-add with correct account

### Verify Chart Trader Matches
- [ ] Chart Trader panel shows **Sim102_Data_Collect**
- [ ] Strategies tab shows **Sim102_Data_Collect**

### Verify Indicator Detection
- [ ] Check Control Center → Log tab for indicator detection messages
- [ ] Confirm no errors about missing required indicators
- [ ] T3ProEquivalent warning is expected and OK

---

## Step 6: Enable Strategy

1. [ ] In Strategies tab, click **Enabled** checkbox for ActiveNikiTrader
2. [ ] Verify Log tab shows:
   ```
   Enabling NinjaScript strategy 'ActiveNikiTrader/...' : On starting a real-time strategy
   ```
3. [ ] Verify **Connection** column shows **Simulation**
4. [ ] Verify strategy panel appears on chart showing:
   - [ ] "⚡ AUTO TRADING ON"
   - [ ] Indicator states (UP/DN)
   - [ ] Confluence count

---

## Step 7: Post-Enable Verification

- [ ] Control Center → Accounts tab → Sim102_Data_Collect visible
- [ ] Strategies tab shows:
  - Account: Sim102_Data_Collect
  - Connection: Simulation
  - Enabled: ✓
- [ ] No error messages in Log tab
- [ ] Partner confirms her charts still show Sim101

---

## During Operation

### Trading Window
- **Active hours:** 10:00 AM - 10:59 AM ET only
- Outside this window, strategy monitors but does not trade

### Monitor Regularly
- [ ] Sim102_Data_Collect P&L in Accounts tab
- [ ] Strategy panel on chart
- [ ] Log file: `C:\Users\Administrator\Documents\NinjaTrader 8\log\ActiveNikiTrader_[date]_[session].txt`

### If Any Doubt
**Immediately disable strategy:**
- Uncheck the Enabled checkbox in Strategies tab
- OR right-click strategy → Disable

---

## End of Session

1. [ ] Disable ActiveNikiTrader strategy (optional — it only trades 10:00-10:59)
2. [ ] Review Sim102_Data_Collect trades in Accounts tab
3. [ ] Compare signals with partner's ActiveNikiMonitor log:
   ```
   grep "SIGNAL" ActiveNikiMonitor_*.txt
   grep "PLACED" ActiveNikiTrader_*.txt
   ```
4. [ ] Record results in Daily Log below

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Strategy shows Sim101 in Strategies tab | Remove strategy, re-add, set Account=Sim102_Data_Collect in config dialog |
| Connection column empty | Normal when disabled; populates after enabling |
| Indicators show "N/A" or "hosted" | Add missing indicators to chart first |
| Strategy disabled itself | Check if manual order placed; check Log for errors |
| Panel not showing | Check chart is on Simulation connection |
| T3Pro missing warning | Expected — using T3ProEquivalent instead |

---

## Emergency Stop

If strategy appears on wrong account:
1. **Immediately:** Uncheck Enabled in Strategies tab
2. Check Orders tab for any pending orders
3. Cancel any unintended orders
4. Check Positions tab — close any unintended positions
5. Review and restart setup from Step 4

---

## Workspace Layout

```
┌─────────────────────────────────────────────────────────────┐
│  VPS Desktop                                                │
├────────────────────────────┬────────────────────────────────┤
│  Partner's Chart           │  Alex's Chart                  │
│  (Simulation - Sim101)     │  (Simulation - Sim102)         │
│                            │                                │
│  - ActiveNikiMonitor       │  - ActiveNikiTrader            │
│  - Manual trading          │  - Auto trading                │
│                            │                                │
│  Account: Sim101           │  Account: Sim102_Data_Collect  │
└────────────────────────────┴────────────────────────────────┘
```

---

## Daily Log

| Date | Sim102 Trades | Sim102 Gross P&L | Slippage | Notes |
|------|---------------|------------------|----------|-------|
| 2026-01-29 | | | | First forward test day |
| | | | | |
| | | | | |
| | | | | |
| | | | | |

---

## Key Lessons Learned

1. **Chart Trader ≠ Strategy Account:** The account shown in Chart Trader panel only affects manual orders. The strategy uses whatever account was set in its configuration dialog.

2. **Always verify in Strategies tab:** Before enabling, always check the **Account display name** column in Control Center → Strategies tab.

3. **Multi-provider required:** To create additional simulation accounts, Multi-provider must be enabled in Tools → Settings → General.

4. **T3ProEquivalent is expected:** The T3Pro warning in logs is normal — T3ProEquivalent is the working substitute.

---

## Strategy Parameters Reference

From Market Replay backtesting (Dec 7, 2025 - Jan 27, 2026):
- **Gross P&L:** +$6,420 (252 trades)
- **Win Rate:** 37.7%
- **Best window:** 10:00-10:59 AM ET
- **Trailing Stop:** 80/40 (activate at +80t, trail 40t)

---

*Document created: January 28, 2026*
