"""
Report generation and multi-day comparison for trading analysis.
Updated to support Market Replay Dates.
"""

import os
import re
from datetime import datetime
from collections import defaultdict

from config import TICK_VALUE, TICK_SIZE
from analysis import (
    analyze_confluence_effectiveness, analyze_trigger_effectiveness,
    analyze_indicator_correlation, analyze_adverse_flips,
    analyze_early_exit_impact, analyze_trailing_stop_impact
)


def find_previous_analyses(folder_path, current_date_str):
    """Find and parse previous analysis files for comparison."""
    abs_folder = os.path.abspath(folder_path.rstrip('/\\'))
    folder_name = os.path.basename(abs_folder)
    parent_dir = os.path.dirname(abs_folder)
    analyses = []
    
    if not parent_dir or not os.path.exists(parent_dir):
        return analyses
    
    is_local = folder_name.endswith('_local')
    folder_pattern = r'^(\d{4}-\d{2}-\d{2})_local$' if is_local else r'^(\d{4}-\d{2}-\d{2})$'
    
    for item in os.listdir(parent_dir):
        item_path = os.path.join(parent_dir, item)
        if not os.path.isdir(item_path):
            continue
        
        match = re.match(folder_pattern, item)
        if not match:
            continue
        
        date_from_folder = match.group(1)
        for f in os.listdir(item_path):
            if f.endswith('_Trading_Analysis.txt'):
                analysis_path = os.path.join(item_path, f)
                stats = parse_analysis_file(analysis_path, date_from_folder)
                if stats:
                    analyses.append(stats)
                break
    
    analyses.sort(key=lambda x: x['date'])
    return analyses


def parse_analysis_file(filepath, date_str):
    """Extract key stats from an existing analysis file."""
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
        
        stats = {'date': date_str, 'filepath': filepath}
        match = re.search(r'Total Round-trips: (\d+)', content)
        stats['trades'] = int(match.group(1)) if match else 0
        match = re.search(r'(\d+\.?\d*)% win rate', content)
        stats['win_rate'] = float(match.group(1)) if match else 0
        match = re.search(r'Total P&L: ([+-]?\d+) ticks', content)
        stats['pnl'] = int(match.group(1)) if match else 0
        
        match = re.search(r'ALIGNED with signals: (\d+) trades, ([+-]?\d+)t', content)
        stats['aligned_count'] = int(match.group(1)) if match else 0
        stats['aligned_pnl'] = int(match.group(2)) if match else 0
        
        match = re.search(r'NO SIGNAL nearby: (\d+) trades, ([+-]?\d+)t', content)
        stats['no_signal_count'] = int(match.group(1)) if match else 0
        stats['no_signal_pnl'] = int(match.group(2)) if match else 0
        
        return stats
    except:
        return None


def generate_report(roundtrips, signals, date_str, folder_path=None, bars=None):
    """Generate the trading analysis report with explicit Market Replay Dates."""
    
    complete_rts = [rt for rt in roundtrips if rt['complete']]
    
    # Identify actual Market Date range from timestamps
    all_dates = []
    if signals: all_dates.extend([s['timestamp'].date() for s in signals])
    if complete_rts: all_dates.extend([rt['entry']['timestamp'].date() for rt in complete_rts])
    
    if all_dates:
        min_date = min(all_dates).strftime("%b %d, %Y")
        max_date = max(all_dates).strftime("%b %d, %Y")
        display_date = min_date.upper() if min_date == max_date else f"{min_date} TO {max_date}".upper()
    else:
        dt = datetime.strptime(date_str, "%Y-%m-%d")
        display_date = dt.strftime("%b %d, %Y").upper()
    
    monitor_signals = [s for s in signals if s['source'] == 'Monitor']
    trader_signals = [s for s in signals if s['source'] == 'Trader']
    trader_orders = [s for s in trader_signals if s['order_placed']]
    
    total_trades = len(complete_rts)
    wins = sum(1 for rt in complete_rts if rt['pnl_ticks'] > 0)
    losses = sum(1 for rt in complete_rts if rt['pnl_ticks'] < 0)
    win_rate = (wins / total_trades * 100) if total_trades > 0 else 0
    total_pnl = sum(rt['pnl_ticks'] for rt in complete_rts)
    
    confluence_stats = analyze_confluence_effectiveness(roundtrips)
    trigger_stats = analyze_trigger_effectiveness(roundtrips)
    indicator_stats = analyze_indicator_correlation(roundtrips)
    adverse_flip_stats = analyze_adverse_flips(roundtrips) if bars else {}
    early_exit_analysis = analyze_early_exit_impact(roundtrips) if bars else None
    trailing_stop_analysis = analyze_trailing_stop_impact(roundtrips) if bars else None
    
    time_buckets = defaultdict(lambda: {'trades': 0, 'wins': 0, 'pnl': 0})
    for rt in complete_rts:
        ts = rt['entry']['timestamp']
        hour, minute = ts.hour, ts.minute
        if hour < 8 or (hour == 8 and minute < 30): bucket = 'Pre-8:30'
        elif hour == 8: bucket = '8:30-9:00'
        elif hour == 9: bucket = '9:00-10:00'
        elif hour == 10: bucket = '10:00-11:00'
        else: bucket = '11:00+'
        time_buckets[bucket]['trades'] += 1
        time_buckets[bucket]['pnl'] += rt['pnl_ticks']
        if rt['pnl_ticks'] > 0: time_buckets[bucket]['wins'] += 1

    lines = []
    lines.append("=" * 100)
    lines.append(f"MARKET REPLAY ANALYSIS: {display_date}")
    lines.append(f"Report Generated (System Time): {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    lines.append("=" * 100)
    lines.append("")
    
    lines.append("SESSION SUMMARY")
    lines.append("-" * 15)
    lines.append(f"Total Round-trips: {total_trades}")
    lines.append(f"Win/Loss:          {wins}W / {losses}L ({win_rate:.1f}% win rate)")
    lines.append(f"Total P&L:         {total_pnl:+.0f} ticks (${total_pnl * TICK_VALUE:+.2f})")
    lines.append(f"Signal count:      Monitor: {len(monitor_signals)}, Trader: {len(trader_signals)} ({len(trader_orders)} placed)")
    lines.append("")
    
    # Trigger and Confluence Sections
    if confluence_stats:
        lines.append("CONFLUENCE LEVEL ANALYSIS")
        lines.append("-" * 25)
        for k in sorted(confluence_stats.keys(), reverse=True):
            s = confluence_stats[k]
            wr = (s['wins']/s['count']*100) if s['count']>0 else 0
            lines.append(f"  {k}: {s['count']} trades, {s['wins']}W ({wr:.0f}%), {s['pnl']:+.0f}t")
        lines.append("")

    # ALL SIGNALS SECTION
    unique_signals = []
    seen = set()
    for sig in signals:
        key = (sig['timestamp'], sig['direction'])
        if key not in seen:
            seen.add(key); unique_signals.append(sig)
    
    lines.append(f"ALL SIGNALS FIRED: {len(unique_signals)}")
    lines.append("-" * 100)
    lines.append(f"{'Market Timestamp':<22} {'Dir':<6} {'Source':<10} {'Trigger':<22} {'Conf':<6} {'Price'}")
    for sig in unique_signals:
        ts = sig['timestamp'].strftime('%Y-%m-%d %H:%M:%S')
        conf = f"{sig['confluence_count']}/{sig['confluence_total']}"
        ord_mark = " [ORDER]" if sig.get('order_placed') else ""
        lines.append(f"{ts:<22} {sig['direction']:<6} {sig['source']:10} {sig['trigger']:22} {conf:6} {sig['price']:.2f}{ord_mark}")
    lines.append("")
    
    # STRATEGY TRADES SECTION
    if total_trades > 0:
        lines.append("STRATEGY TRADES (FILLED)")
        lines.append("-" * 100)
        lines.append(f"{'Market Entry Time':<22} {'Dir':<6} {'P&L Ticks':<10} {'P&L $':<12} {'Result'} {'Signal Info'}")
        for rt in sorted(complete_rts, key=lambda x: x['entry']['timestamp']):
            ts = rt['entry']['timestamp'].strftime('%Y-%m-%d %H:%M:%S')
            pnl_t = rt['pnl_ticks']
            res = "[WIN]" if pnl_t > 0 else "[LOSS]"
            sig = rt.get('signal')
            sig_i = f"[{sig['trigger']}] {sig['confluence_count']}/{sig['confluence_total']}" if sig else "[NO SIGNAL]"
            lines.append(f"{ts:<22} {rt['direction']:<6} {pnl_t:<10.0f} ${pnl_t*TICK_VALUE:<11.2f} {res:7} {sig_i}")
        lines.append("")

    # SIGNAL ALIGNMENT
    aligned = [rt for rt in complete_rts if rt['alignment'] == 'ALIGNED']
    counter = [rt for rt in complete_rts if rt['alignment'] == 'COUNTER']
    no_signal = [rt for rt in complete_rts if rt['alignment'] == 'NO_SIGNAL']
    
    lines.append("SIGNAL ALIGNMENT SUMMARY")
    lines.append("-" * 25)
    lines.append(f"ALIGNED with signals:   {len(aligned)} trades, {sum(r['pnl_ticks'] for r in aligned):+.0f}t")
    lines.append(f"COUNTER to signals:     {len(counter)} trades, {sum(r['pnl_ticks'] for r in counter):+.0f}t")
    lines.append(f"NO SIGNAL nearby:       {len(no_signal)} trades, {sum(r['pnl_ticks'] for r in no_signal):+.0f}t")
    lines.append("")

    # SLIPPAGE SECTION
    e_slips = [rt.get('entry_slippage_ticks', 0) for rt in complete_rts if rt.get('entry_slippage_ticks') is not None]
    x_slips = [rt.get('exit_slippage_ticks', 0) for rt in complete_rts if rt.get('exit_slippage_ticks') is not None]
    if e_slips or x_slips:
        total_slip = sum(e_slips) + sum(x_slips)
        lines.append("COST ANALYSIS")
        lines.append("-" * 15)
        lines.append(f"Gross P&L:       {total_pnl:+.0f} ticks")
        lines.append(f"Total Slippage:  {total_slip:+.0f} ticks (${total_slip * TICK_VALUE:+.2f})")
        lines.append(f"Broker Fees:     ${total_trades * 4.50:.2f} (est. $4.50/RT)")
        lines.append(f"NET P&L:         ${(total_pnl * TICK_VALUE) - (total_slip * TICK_VALUE) - (total_trades * 4.50):+.2f}")
        lines.append("")

    # MULTI-DAY COMPARISON
    if folder_path:
        prev = find_previous_analyses(folder_path, date_str)
        prev = [a for a in prev if a['date'] < date_str][-5:]
        if prev:
            lines.append("=" * 100)
            lines.append("MULTI-DAY HISTORY (PREVIOUS SESSIONS)")
            lines.append("-" * 100)
            lines.append(f"{'Date':<12} {'Trades':<8} {'Win%':<8} {'P&L':<10} {'Alignment (Aligned / No Signal)'}")
            for s in prev:
                lines.append(f"{s['date']:<12} {s['trades']:<8} {s['win_rate']:>3.0f}%    {s['pnl']:>+5.0f}t     {s['aligned_count']} aligned ({s['aligned_pnl']:+d}t), {s['no_signal_count']} no signal")
            lines.append("-" * 100)
            lines.append(f"{'TODAY':<12} {total_trades:<8} {win_rate:>3.0f}%    {total_pnl:>+5.0f}t     {len(aligned)} aligned, {len(no_signal)} no signal")

    lines.append("")
    lines.append("=" * 100)
    return "\n".join(lines)
