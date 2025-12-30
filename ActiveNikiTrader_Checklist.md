# ActiveNikiTrader - Quick Setup Checklist
## Print this and check off each item!

---

## ⚡ CRITICAL SETTINGS (Check First!)

□ Stop Loss USD = **80**
□ Take Profit USD = **200**
□ Min Confluence For Auto Trade = **5**
□ Session 2 End Hour = **16**
□ EOD Close Hour = **15**
□ Use Time-Based Cooldown = **True** ✓
□ Cooldown Seconds = **120**
□ Enable Daily Loss Limit = **True** ✓
□ Daily Loss Limit USD = **300**

---

## 📋 PHASE 1 (Signals Only)

□ Enable Auto Trading = **False**
□ Account = **Sim101** or **Playback101**

---

## 📋 PHASE 2 (Auto Trade)

□ Enable Auto Trading = **True**
□ Account = **Sim101**

---

## 📊 UNIRENKO CHART SETTINGS

□ Type = UniRenko
□ Tick Trend = **10**
□ Open Offset = **27**
□ Tick Reversal = **50**

---

## 🔧 INDICATORS ON CHART

□ RubyRiverEquivalent
□ DragonTrendEquivalent  
□ VIDYAProEquivalent
□ EasyTrendEquivalent
□ SolarWaveEquivalent
□ T3ProEquivalent
□ AIQ_1Equivalent

---

## ⏰ TRADING HOURS

□ Session 1: **7:00 - 8:29**
□ Session 2: **9:00 - 16:00**
□ News Close: **8:28**
□ EOD Close: **15:58**

---

## ✅ PRE-FLIGHT VERIFICATION

After adding strategy, check log shows:
□ "Trading Hours: 07:00-08:29, 09:00-16:00"
□ "Auto-Close EOD: 15:58"
□ "Daily Loss Limit: $300"
□ SL=$80 TP=$200 in signal boxes

---

## ❌ COMMON MISTAKES

| Wrong | Correct |
|-------|---------|
| SL=100, TP=60 | SL=80, TP=200 |
| Session 2 End=11 | Session 2 End=16 |
| EOD Close Hour=10 | EOD Close Hour=15 |
| Time-Based Cooldown=False | Time-Based Cooldown=True |
| Auto Trade Confluence=3 | Auto Trade Confluence=5 |

---

*Keep this checklist handy when configuring!*
