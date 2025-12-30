# ActiveNikiTrader - Winning Configuration Reference
## Version: December 2025 | Result: +$950 on UniRenko

---

## QUICK CHECKLIST ✅

Before running, verify these critical settings:

| # | Setting | CORRECT Value | ❌ Common Mistake |
|---|---------|---------------|-------------------|
| 1 | Stop Loss USD | **80** | 100 (too high) |
| 2 | Take Profit USD | **200** | 60 (too low, inverts R:R) |
| 3 | Min Confluence For Auto Trade | **5** | 3 or 4 (too aggressive) |
| 4 | Session 2 End Hour | **16** | 11 (misses afternoon) |
| 5 | EOD Close Hour | **15** | 10 (closes too early) |
| 6 | Use Time-Based Cooldown | **True** | False (causes rapid-fire) |
| 7 | Enable Auto Trading | **False** (Phase 1) | True |

---

## FULL CONFIGURATION

### 1. Signal Filters
| Parameter | Value | Notes |
|-----------|-------|-------|
| Min Confluence Required | 4 | Minimum to generate signal |
| Max Bars After Yellow Square | 3 | Window for RR confirmation |
| Min Solar Wave Count | 1 | Minimum wave count |
| **Stop Loss USD** | **80** | 4 points on NQ ($20/pt) |
| **Take Profit USD** | **200** | 10 points on NQ |
| Cooldown Bars | 10 | Used if time-based is off |
| **Enable Auto Trading** | **False** | Set True for Phase 2 |
| **Min Confluence For Auto Trade** | **5** | Higher quality trades |
| Use Trading Hours Filter | True | Restrict to good hours |

### Trading Hours
| Parameter | Value | Notes |
|-----------|-------|-------|
| Session 1 Start Hour | 7 | 7:00 AM |
| Session 1 Start Minute | 0 | |
| Session 1 End Hour | 8 | |
| Session 1 End Minute | 29 | 8:29 AM (before news) |
| Session 2 Start Hour | 9 | 9:00 AM (after news) |
| Session 2 Start Minute | 0 | |
| **Session 2 End Hour** | **16** | 4:00 PM |
| Session 2 End Minute | 0 | |

### Auto-Close Settings
| Parameter | Value | Notes |
|-----------|-------|-------|
| Close Before News | True | Protect from 8:30 volatility |
| News Close Hour | 8 | |
| News Close Minute | 28 | 8:28 AM |
| Close At End Of Day | True | No overnight positions |
| **EOD Close Hour** | **15** | 3:58 PM |
| EOD Close Minute | 58 | |

---

### 2. Indicator Selection
| Parameter | Value |
|-----------|-------|
| Use Ruby River | ✅ True |
| Use Dragon Trend | ✅ True |
| Use Solar Wave | ✅ True |
| Use VIDYA Pro | ✅ True |
| Use Easy Trend | ✅ True |
| Use T3 Pro | ✅ True |

---

### 3. T3 Pro Settings
| Parameter | Value |
|-----------|-------|
| T3Pro Period | 14 |
| T3Pro TCount | 3 |
| T3Pro VFactor | 0.7 |
| T3Pro Chaos Smoothing | True |
| T3Pro Chaos Period | 5 |
| T3Pro Filter Enabled | True |
| T3Pro Filter Multiplier | 4 |

---

### 4. VIDYA Pro Settings
| Parameter | Value |
|-----------|-------|
| VIDYA Period | 9 |
| VIDYA Volatility Period | 9 |
| VIDYA Smoothing Enabled | True |
| VIDYA Smoothing Period | 5 |
| VIDYA Filter Enabled | True |
| VIDYA Filter Multiplier | 4 |

---

### 5. Easy Trend Settings
| Parameter | Value |
|-----------|-------|
| EasyTrend Period | 30 |
| EasyTrend Smoothing Enabled | True |
| EasyTrend Smoothing Period | 7 |
| EasyTrend Filter Enabled | True |
| EasyTrend Filter Multiplier | 0.5 |
| EasyTrend ATR Period | 100 |

---

### 6. Ruby River Settings
| Parameter | Value |
|-----------|-------|
| RubyRiver MA Period | 20 |
| RubyRiver Smoothing Enabled | True |
| RubyRiver Smoothing Period | 5 |
| RubyRiver Offset Multiplier | 0.15 |
| RubyRiver Offset Period | 100 |

---

### 7. Dragon Trend Settings
| Parameter | Value |
|-----------|-------|
| DragonTrend Period | 10 |
| DragonTrend Smoothing Enabled | True |
| DragonTrend Smoothing Period | 5 |

---

### 8. Solar Wave Settings
| Parameter | Value |
|-----------|-------|
| SolarWave ATR Period | 100 |
| SolarWave Trend Multiplier | 2 |
| SolarWave Stop Multiplier | 4 |

---

### 9. Alerts
| Parameter | Value |
|-----------|-------|
| Enable Sound Alert | True |

---

### 10. UniRenko Settings
| Parameter | Value | Notes |
|-----------|-------|-------|
| **UniRenko Mode** | **True** | Enable for Renko bars |
| **Use Time-Based Cooldown** | **True** | IMPORTANT for Renko! |
| **Cooldown Seconds** | **120** | 2 minutes between trades |
| Log Bar Details | False | True only for debugging |

---

### 11. Risk Management
| Parameter | Value | Notes |
|-----------|-------|-------|
| **Enable Daily Loss Limit** | **True** | Safety net |
| **Daily Loss Limit USD** | **300** | Stop trading after -$300 |
| Reset Daily P&L at Session | True | Reset each day |

---

## DATA SERIES (UniRenko Chart)

| Parameter | Value |
|-----------|-------|
| Type | UniRenko |
| Price based on | Last |
| Tick Trend | 10 |
| Open Offset | 27 |
| Tick Reversal | 50 |
| Days to load | 3 |
| Break at EOD | True |

**Shorthand:** UniRenko 10-27-50

---

## SETUP SECTION

| Parameter | Value |
|-----------|-------|
| Account | Sim101 (VPS) or Playback101 (Replay) |
| Calculate | On bar close |
| Maximum bars look back | 256 |
| Bars required to trade | 20 |
| Start behavior | Wait until flat |
| Entries per direction | 1 |
| Entry handling | All entries |
| Exit on session close | True |
| Exit on session close seconds | 30 |
| Stop & target submission | Per entry execution |
| Time in force | GTC |

---

## INDICATORS REQUIRED ON CHART

For the strategy to use chart-attached indicators (better than hosted):

1. **RubyRiverEquivalent**
2. **DragonTrendEquivalent**
3. **VIDYAProEquivalent**
4. **EasyTrendEquivalent**
5. **SolarWaveEquivalent**
6. **T3ProEquivalent**
7. **AIQ_1Equivalent**
8. AIQ_SuperBandsEquivalent (optional, for visual)

Or if ninZa license is active:
- ninZaRubyRiver, ninZaDragonTrend, ninZaVIDYAPro
- ninZaEasyTrend, ninZaSolarWave, ninZaT3Pro
- AIQ_1 (native)

---

## PHASE 1 vs PHASE 2

| Setting | Phase 1 (Watch) | Phase 2 (Trade) |
|---------|-----------------|-----------------|
| Enable Auto Trading | **False** | **True** |
| Account | Sim101 | Sim101 |
| Purpose | Validate signals | Test execution |

---

## TROUBLESHOOTING

### Too Many Trades?
- Check: Use Time-Based Cooldown = True
- Check: Cooldown Seconds ≥ 120
- Check: Min Confluence For Auto Trade ≥ 5

### Losing Money?
- Check: Stop Loss = 80 (not higher)
- Check: Take Profit = 200 (not lower)
- R:R should be 2.5:1 (win $200, risk $80)

### No Trades During Afternoon?
- Check: Session 2 End Hour = 16 (not 11)
- Check: EOD Close Hour = 15 (not 10)

### Strategy Terminated?
- Check: Account has market data subscription
- Check: Using Sim101 or Playback account

---

## TESTED RESULTS

| Environment | Bar Type | P&L |
|-------------|----------|-----|
| Local | 5-minute | +$580 |
| VPS | 5-minute | +$380 |
| **VPS** | **UniRenko 10-27-50** | **+$950** ✅ |

---

*Last Updated: December 30, 2025*
