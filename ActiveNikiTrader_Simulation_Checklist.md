# ActiveNikiTrader Simulation Setup Checklist

## Purpose
Run ActiveNikiTrader strategy on Simulation connection (Sim101) without interfering with discretionary live trading on the same VPS.

---

## Pre-Setup Verification

- [ ] Partner's live trading charts are on **live broker connection** (e.g., Tradovate, AMP)
- [ ] Partner is using **ActiveNikiMonitor** (indicator), NOT ActiveNikiTrader (strategy)
- [ ] Confirm live account number displayed in Control Center

---

## Step 1: Connect to Simulation

1. [ ] Control Center → Connections → **Simulation** → Connect
2. [ ] Verify "Sim101" appears in Accounts tab
3. [ ] Confirm both connections show green status:
   - [ ] Live broker: Connected
   - [ ] Simulation: Connected

---

## Step 2: Create Dedicated Chart for Strategy

1. [ ] Control Center → New → Chart
2. [ ] Select instrument: **NQ** (or your instrument)
3. [ ] Set timeframe as needed
4. [ ] **CRITICAL:** In chart toolbar, set connection to **Simulation**
   - Look for connection dropdown (usually shows broker name)
   - Change to "Simulation"
5. [ ] Verify chart title bar shows "Simulation" or "Sim"

---

## Step 3: Add Required Indicators to Chart

Add these indicators (same as partner's chart):
- [ ] AIQ_1
- [ ] ninZaRubyRiver
- [ ] ninZaDragonTrend
- [ ] ninZaVIDYAPro
- [ ] ninZaEasyTrend
- [ ] ninZaSolarWave
- [ ] ninZaT3Pro
- [ ] ninZaAAATrendSync
- [ ] AIQ_SuperBands

---

## Step 4: Add ActiveNikiTrader Strategy

1. [ ] Right-click chart → Strategies → Add Strategy
2. [ ] Select **ActiveNikiTrader**
3. [ ] **CRITICAL SETTINGS:**

| Setting | Value | Verify |
|---------|-------|--------|
| Account | **Sim101** | ⚠️ NOT live account |
| Enabled | False (initially) | |
| EnableAutoTrading | True | |

4. [ ] Configure parameters (match your Market Replay settings):

| Parameter | Your Value |
|-----------|------------|
| MinConfluenceRequired | ___ |
| MinConfluenceForAutoTrade | ___ |
| Session 1 Start Hour | 10 |
| Session 1 Start Minute | 0 |
| Session 1 End Hour | 10 |
| Session 1 End Minute | 59 |
| StopLossUSD | ___ |
| TakeProfitUSD | ___ |
| EnableTrailingStop | ___ |

5. [ ] Click OK to add (still disabled)

---

## Step 5: Pre-Enable Safety Check

### Verify Account Assignment
- [ ] Strategies tab shows "Sim101" for ActiveNikiTrader
- [ ] Live account is NOT listed next to ActiveNikiTrader

### Verify Chart Separation
- [ ] Your Simulation chart: Shows "Simulation" connection
- [ ] Partner's charts: Show live broker connection
- [ ] Charts are in **separate windows** (recommended)

### Verify Indicator Detection
- [ ] Check NinjaTrader Output window for indicator detection messages
- [ ] Confirm strategy found chart-attached indicators (not using hosted equivalents)

---

## Step 6: Enable Strategy

1. [ ] In Strategies tab, right-click ActiveNikiTrader
2. [ ] Click **Enable**
3. [ ] Verify panel appears on chart showing:
   - [ ] "⚡ AUTO TRADING ON"
   - [ ] Indicator states (UP/DN)
   - [ ] Correct MinConfluence settings

---

## Step 7: Post-Enable Verification

- [ ] Control Center → Account tab → Sim101 selected
- [ ] Check "Positions" shows only Simulation account
- [ ] Check "Orders" shows only Simulation account
- [ ] Partner confirms her Account tab shows live account

---

## During Operation

### Monitor Regularly
- [ ] Sim101 P&L in Account tab
- [ ] Strategy panel on chart
- [ ] Log file: `ActiveNikiTrader_[date]_[session].txt`

### If Any Doubt
**Immediately disable strategy:**
- Right-click strategy → Disable
- OR click "Enabled" checkbox off in Strategies window

---

## End of Session

1. [ ] Disable ActiveNikiTrader strategy
2. [ ] Review Sim101 trades in Account Performance
3. [ ] Compare signals with partner's ActiveNikiMonitor log:
   ```
   grep "SIGNAL" ActiveNikiMonitor_*.txt
   grep "PLACED" ActiveNikiTrader_*.txt
   ```

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Strategy shows wrong account | Remove strategy, re-add, carefully select Sim101 |
| Indicators show "N/A" or "hosted" | Add missing indicators to chart first |
| Strategy disabled itself | Check if partner placed manual order on same chart |
| Panel not showing | Check chart is on Simulation connection |

---

## Emergency Stop

If strategy appears on wrong account:
1. **Immediately:** Control Center → Strategies → Disable All
2. Check Orders tab for any pending orders
3. Cancel any unintended orders
4. Review and restart setup

---

## Chart Layout Recommendation

```
┌─────────────────────────────────────────────────────┐
│  VPS Desktop                                        │
├────────────────────────┬────────────────────────────┤
│  Partner's Chart       │  Your Chart                │
│  (Live Broker)         │  (Simulation)              │
│                        │                            │
│  - ActiveNikiMonitor   │  - ActiveNikiTrader        │
│  - Manual trading      │  - Auto trading (Sim101)   │
│                        │                            │
│  Account: Live         │  Account: Sim101           │
└────────────────────────┴────────────────────────────┘
```

---

## Daily Log

| Date | Sim101 Trades | Sim101 P&L | Notes |
|------|---------------|------------|-------|
| | | | |
| | | | |
| | | | |

---

*Last updated: January 2026*
