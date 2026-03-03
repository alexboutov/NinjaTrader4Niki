"""
Report generation and multi-day comparison for trading analysis.
Updated to support Market Replay Dates and restored Order Markers.
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
    abs_folder = os.path.abspath(folder_path.rstrip('/\\'))
    folder_name = os.path.basename(abs_folder)
    parent_dir = os.path.dirname(abs_folder)
    analyses = []
    if not parent_dir or not os.path.exists(parent_dir): return analyses
    is_local = folder_name.endswith('_local')
    folder_pattern = r'^(\d{4}-\d{2}-\d{2})_local$' if is_local else r'^(\d{4}-\d{2}-\d{2})$'
    for item in os.listdir(parent_dir):
        item_path = os.path.join(parent_dir, item)
        if not os.path.isdir(item_path): continue
        match = re.match(folder_pattern, item)
        if not match: continue
        date_from_folder = match.group(1)
        for f in os.listdir(item_path):
            if f.endswith('_Trading_Analysis.txt'):
                stats = parse_analysis_file(os.path.join(item_path, f), date_from_folder)
                if stats: analyses.append(stats)
                break
    analyses.sort(key=lambda x: x['date'])
    return analyses

def parse_analysis_file(filepath, date_str):
    try:
        with open(filepath, 'r', encoding='utf-8') as f: content = f.read()
        stats = {'date': date_str, 'filepath': filepath}
        m = re.search(r'Total Round-trips: (\d+)', content)
        stats['trades'] = int(m.group(1)) if m else 0
        m = re.search(r'(\d+\.?\d*)% win rate', content)
        stats['win_rate'] = float(m.group(1)) if m else 0
        m = re.search(r'Total P&L: ([+-]?\d+) ticks', content)
        stats['pnl'] = int(m.group(1)) if m else 0
        m = re.search(r'ALIGNED with signals: (\d+) trades, ([+-]?\d+)t', content)
        stats['aligned_count'] = int(m.group(1)) if m else 0
        stats['aligned_pnl'] = int(m.group(2)) if m else 0
        m = re.search(r'NO SIGNAL nearby: (\d+) trades, ([+-]?\d+)t', content)
        stats['no_signal_count'] = int(m.group(1)) if m else 0
        stats['no_signal_pnl'] = int(m.group(2)) if m else 0
        return stats
    except: return None

def generate_report(roundtrips, signals, date_str, folder_path=None, bars=None):
    complete_rts = [rt for rt in roundtrips if rt['complete']]
    all_dates = []
    if signals: all_dates.extend([s['timestamp'].date() for s in signals])
    if complete_rts: all_dates.extend([rt['entry']['timestamp'].date() for rt in complete_rts])
    if all_dates:
        min_d, max_d = min(all_dates).strftime("%b %d, %Y"), max(all_dates).strftime("%b %d, %Y")
        display_date = min_d.upper() if min_d == max_d else f"{min_d} TO {max_d}".upper()
    else:
        display_date = datetime.strptime(date_str, "%Y-%m-%d").strftime("%b %d, %Y").upper()
    
    total_trades = len(complete_rts)
    wins = sum(1 for rt in complete_rts if rt['pnl_ticks'] > 0)
    win_rate = (wins / total_trades * 100) if total_trades > 0 else 0
    total_pnl = sum(rt['pnl_ticks'] for rt in complete_rts)
    
    lines = []
    lines.append("=" * 100)
    lines.append(f"MARKET REPLAY ANALYSIS: {display_date}")
    lines.append(f"Report Generated (System Time): {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    lines.append("=" * 100)
    lines.append("")
    
    lines.append("SESSION SUMMARY")
    lines.append("-" * 15)
    lines.append(f"Total Round-trips: {total_trades}")
    lines.append(f"Win/Loss:          {wins}W / {total_trades - wins}L ({win_rate:.1f}% win rate)")
    lines.append(f"Total P&L:         {total_pnl:+.0f} ticks (${total_pnl * TICK_VALUE:+.2f})")
    lines.append("")

    # ALL SIGNALS SECTION
    unique_signals_dict = {}
    for sig in signals:
        key = (sig['timestamp'], sig['direction'])
        if key not in unique_signals_dict or sig.get('order_placed'):
            unique_signals_dict[key] = sig
    unique_signals = sorted(unique_signals_dict.values(), key=lambda x: x['timestamp'])
    
    lines.append(f"ALL SIGNALS FIRED: {len(unique_signals)}")
    lines.append("-" * 100)
    lines.append(f"{'Market Timestamp':<22} {'Dir':<6} {'Source':<10} {'Trigger':<22} {'Conf':<6} {'Price'}")
    for sig in unique_signals:
        ts_str = sig['timestamp'].strftime('%Y-%m-%d %H:%M:%S')
        conf = f"{sig['confluence_count']}/8"
        ord_mark = " >>>" if sig.get('order_placed') else ""
        lines.append(f"{ts_str:<22} {sig['direction']:<6} {sig['source']:10} {sig['trigger']:22} {conf:6} {sig['price']:<10.2f}{ord_mark}")
    lines.append("  (>>> = order placed by ActiveNikiTrader)")
    lines.append("")
    
    # STRATEGY TRADES SECTION
    if total_trades > 0:
        lines.append("STRATEGY TRADES (FILLED)")
        lines.append("-" * 100)
        lines.append(f"{'Market Entry Time':<22} {'Dir':<6} {'P&L Ticks':<10} {'P&L $':<12} {'Result'} {'Signal Info'}")
        for rt in sorted(complete_rts, key=lambda x: x['entry']['timestamp']):
            ts_str = rt['entry']['timestamp'].strftime('%Y-%m-%d %H:%M:%S')
            pnl_t = rt['pnl_ticks']
            sig = rt.get('signal')
            sig_i = f"[{sig['trigger']}] {sig['confluence_count']}/8" if sig else "[NO SIGNAL]"
            lines.append(f"{ts_str:<22} {rt['direction']:<6} {pnl_t:<10.0f} ${pnl_t*TICK_VALUE:<11.2f} {'[WIN]' if pnl_t > 0 else '[LOSS]':7} {sig_i}")
        lines.append("")

    lines.append("=" * 100)
    return "\n".join(lines)