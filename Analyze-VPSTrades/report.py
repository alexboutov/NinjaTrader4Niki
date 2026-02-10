"""
Report generation and multi-day comparison for trading analysis.
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
    """Find and parse previous analysis files for comparison.
    
    If analyzing a _local folder, only compare with other _local folders.
    If analyzing a VPS folder (no suffix), only compare with other VPS folders.
    """
    # Get absolute path first, then find parent
    abs_folder = os.path.abspath(folder_path.rstrip('/\\'))
    folder_name = os.path.basename(abs_folder)
    parent_dir = os.path.dirname(abs_folder)
    analyses = []
    
    # Safety check
    if not parent_dir or not os.path.exists(parent_dir):
        return analyses
    
    # Detect if we're analyzing a _local folder
    is_local = folder_name.endswith('_local')
    
    # Set pattern based on folder type
    if is_local:
        # Match YYYY-MM-DD_local folders
        folder_pattern = r'^(\d{4}-\d{2}-\d{2})_local$'
    else:
        # Match YYYY-MM-DD folders (no suffix)
        folder_pattern = r'^(\d{4}-\d{2}-\d{2})$'
    
    # Look for dated folders with analysis files
    for item in os.listdir(parent_dir):
        item_path = os.path.join(parent_dir, item)
        if not os.path.isdir(item_path):
            continue
        
        # Check if folder name matches the appropriate pattern
        match = re.match(folder_pattern, item)
        if not match:
            continue
        
        # Extract the date portion
        date_from_folder = match.group(1)
        
        # Look for analysis file
        for f in os.listdir(item_path):
            if f.endswith('_Trading_Analysis.txt'):
                analysis_path = os.path.join(item_path, f)
                stats = parse_analysis_file(analysis_path, date_from_folder)
                if stats:
                    analyses.append(stats)
                break
    
    # Sort by date
    analyses.sort(key=lambda x: x['date'])
    
    return analyses


def parse_analysis_file(filepath, date_str):
    """Extract key stats from an existing analysis file."""
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
        
        stats = {'date': date_str, 'filepath': filepath}
        
        # Extract total trades
        match = re.search(r'Total Round-trips: (\d+)', content)
        stats['trades'] = int(match.group(1)) if match else 0
        
        # Extract win rate
        match = re.search(r'(\d+\.?\d*)% win rate', content)
        stats['win_rate'] = float(match.group(1)) if match else 0
        
        # Extract total P&L
        match = re.search(r'Total P&L: ([+-]?\d+) ticks', content)
        stats['pnl'] = int(match.group(1)) if match else 0
        
        # Extract aligned stats
        match = re.search(r'ALIGNED with signals: (\d+) trades, ([+-]?\d+)t', content)
        if match:
            stats['aligned_count'] = int(match.group(1))
            stats['aligned_pnl'] = int(match.group(2))
        else:
            stats['aligned_count'] = 0
            stats['aligned_pnl'] = 0
        
        # Extract no signal stats
        match = re.search(r'NO SIGNAL nearby: (\d+) trades, ([+-]?\d+)t', content)
        if match:
            stats['no_signal_count'] = int(match.group(1))
            stats['no_signal_pnl'] = int(match.group(2))
        else:
            stats['no_signal_count'] = 0
            stats['no_signal_pnl'] = 0
        
        return stats
    except Exception as e:
        return None


def generate_report(roundtrips, signals, date_str, folder_path=None, bars=None):
    """Generate the trading analysis report."""
    
    # Filter complete round-trips
    complete_rts = [rt for rt in roundtrips if rt['complete']]
    
    # Separate signals by source
    monitor_signals = [s for s in signals if s['source'] == 'Monitor']
    trader_signals = [s for s in signals if s['source'] == 'Trader']
    trader_orders = [s for s in trader_signals if s['order_placed']]
    
    # Basic stats
    total_trades = len(complete_rts)
    wins = sum(1 for rt in complete_rts if rt['pnl_ticks'] > 0)
    losses = sum(1 for rt in complete_rts if rt['pnl_ticks'] < 0)
    win_rate = (wins / total_trades * 100) if total_trades > 0 else 0
    
    total_pnl = sum(rt['pnl_ticks'] for rt in complete_rts)
    long_rts = [rt for rt in complete_rts if rt['direction'] == 'LONG']
    short_rts = [rt for rt in complete_rts if rt['direction'] == 'SHORT']
    long_pnl = sum(rt['pnl_ticks'] for rt in long_rts)
    short_pnl = sum(rt['pnl_ticks'] for rt in short_rts)
    
    # Alignment stats
    aligned = [rt for rt in complete_rts if rt['alignment'] == 'ALIGNED']
    counter = [rt for rt in complete_rts if rt['alignment'] == 'COUNTER']
    no_signal = [rt for rt in complete_rts if rt['alignment'] == 'NO_SIGNAL']
    
    aligned_pnl = sum(rt['pnl_ticks'] for rt in aligned)
    counter_pnl = sum(rt['pnl_ticks'] for rt in counter)
    no_signal_pnl = sum(rt['pnl_ticks'] for rt in no_signal)
    
    # Confluence analysis
    confluence_stats = analyze_confluence_effectiveness(roundtrips)
    trigger_stats = analyze_trigger_effectiveness(roundtrips)
    indicator_stats = analyze_indicator_correlation(roundtrips)
    
    # Adverse flip analysis (if BAR data available)
    adverse_flip_stats = analyze_adverse_flips(roundtrips) if bars else {}
    early_exit_analysis = analyze_early_exit_impact(roundtrips) if bars else None
    
    # Trailing stop analysis (if BAR data available)
    trailing_stop_analysis = analyze_trailing_stop_impact(roundtrips) if bars else None
    
    # Best/worst trades
    sorted_by_pnl = sorted(complete_rts, key=lambda x: x['pnl_ticks'], reverse=True)
    top_5 = sorted_by_pnl[:5]
    bottom_5 = sorted_by_pnl[-5:]
    
    # Time-based analysis
    time_buckets = defaultdict(lambda: {'trades': 0, 'wins': 0, 'pnl': 0})
    for rt in complete_rts:
        hour = rt['entry']['timestamp'].hour
        minute = rt['entry']['timestamp'].minute
        
        if hour < 8 or (hour == 8 and minute < 30):
            bucket = 'Pre-8:30'
        elif hour == 8:
            bucket = '8:30-9:00'
        elif hour == 9:
            bucket = '9:00-10:00'
        elif hour == 10:
            bucket = '10:00-11:00'
        else:
            bucket = '11:00+'
        
        time_buckets[bucket]['trades'] += 1
        time_buckets[bucket]['pnl'] += rt['pnl_ticks']
        if rt['pnl_ticks'] > 0:
            time_buckets[bucket]['wins'] += 1
    
    # Format date for display
    dt = datetime.strptime(date_str, "%Y-%m-%d")
    display_date = dt.strftime("%b %d, %Y").upper()
    
    # Build report
    lines = []
    lines.append("=" * 80)
    lines.append(f"{display_date} TRADING SESSION ANALYSIS")
    lines.append("=" * 80)
    lines.append("")
    
    # Signal sources summary
    lines.append("SIGNAL SOURCES")
    lines.append("-" * 14)
    lines.append(f"ActiveNikiMonitor signals: {len(monitor_signals)}")
    lines.append(f"ActiveNikiTrader signals:  {len(trader_signals)}")
    lines.append(f"  - Orders placed:         {len(trader_orders)}")
    lines.append(f"  - Outside hours:         {len([s for s in trader_signals if s['blocked_reason'] == 'OUTSIDE_HOURS'])}")
    lines.append(f"  - Blocked by cooldown:   {len([s for s in trader_signals if s['blocked_reason'] == 'COOLDOWN'])}")
    if bars:
        lines.append(f"BAR data loaded:           {len(bars)} bars from CSV")
    lines.append("")
    
    lines.append("SESSION SUMMARY")
    lines.append("-" * 15)
    lines.append(f"Total Round-trips: {total_trades}")
    lines.append(f"Win/Loss: {wins}W / {losses}L ({win_rate:.1f}% win rate)")
    lines.append(f"Total P&L: {total_pnl:+.0f} ticks (${total_pnl * TICK_VALUE:+.2f})")
    lines.append(f"  LONG:  {len(long_rts)} trades, {long_pnl:+.0f}t")
    lines.append(f"  SHORT: {len(short_rts)} trades, {short_pnl:+.0f}t")
    lines.append("")
    
    # Confluence analysis
    if confluence_stats:
        lines.append("CONFLUENCE LEVEL ANALYSIS")
        lines.append("-" * 25)
        for conf_key in sorted(confluence_stats.keys(), reverse=True):
            stats = confluence_stats[conf_key]
            wr = (stats['wins'] / stats['count'] * 100) if stats['count'] > 0 else 0
            lines.append(f"  {conf_key}: {stats['count']} trades, {stats['wins']}W ({wr:.0f}%), {stats['pnl']:+.0f}t")
        lines.append("")
    
    # Trigger type analysis
    if trigger_stats:
        lines.append("TRIGGER TYPE ANALYSIS")
        lines.append("-" * 21)
        for trigger, stats in sorted(trigger_stats.items(), key=lambda x: x[1]['pnl'], reverse=True):
            wr = (stats['wins'] / stats['count'] * 100) if stats['count'] > 0 else 0
            lines.append(f"  {trigger}: {stats['count']} trades, {stats['wins']}W ({wr:.0f}%), {stats['pnl']:+.0f}t")
        lines.append("")
    
    # All signals section - deduplicate by (time, direction)
    unique_signals = []
    seen = set()
    for sig in signals:
        key = (sig['time_str'], sig['direction'])
        if key not in seen:
            seen.add(key)
            unique_signals.append(sig)
    
    lines.append(f"ALL SIGNALS FIRED: {len(unique_signals)}")
    lines.append("-" * 40)
    lines.append("Time      Dir   Source   Trigger              Conf   Price")
    for sig in unique_signals:
        conf_str = f"{sig['confluence_count']}/{sig['confluence_total']}"
        order_marker = " ►" if sig.get('order_placed') else ""
        lines.append(f"{sig['time_str']}  {sig['direction']:5} {sig['source']:8} [{sig['trigger']:18}] {conf_str:5} {sig['price']:.2f}{order_marker}")
    lines.append("  (► = order placed by ActiveNikiTrader)")
    lines.append("")
    
    # Strategy trades section with BAR data
    if total_trades > 0:
        lines.append("STRATEGY TRADES")
        lines.append("-" * 15)
        for rt in complete_rts:
            pnl_marker = "✓" if rt['pnl_ticks'] > 0 else "✗"
            sig = rt.get('signal')
            if sig:
                sig_info = f"[{sig['trigger']}] {sig['confluence_count']}/{sig['confluence_total']}"
            else:
                sig_info = "[NO SIGNAL]"
            
            # Add exit trigger info if available (show first one that fired)
            exit_info = ""
            confluence_drop = rt.get('flip_analysis', {}).get('confluence_drop')
            first_flip = rt.get('flip_analysis', {}).get('first_adverse_flip')
            
            if confluence_drop:
                hypo = confluence_drop['hypothetical_pnl_ticks']
                diff = rt.get('confluence_exit_difference', 0)
                conf_change = f"{confluence_drop['entry_confluence']}→{confluence_drop['exit_confluence']}"
                if diff and diff > 0:
                    exit_info = f" [conf {conf_change}: save {diff:+.0f}t]"
                elif diff and diff < 0:
                    exit_info = f" [conf {conf_change}: cost {abs(diff):.0f}t]"
            elif first_flip:
                hypo = first_flip['hypothetical_pnl_ticks']
                diff = rt.get('flip_exit_difference', 0)
                if diff and diff > 0:
                    exit_info = f" [{first_flip['indicator']} flip: save {diff:+.0f}t]"
                elif diff and diff < 0:
                    exit_info = f" [{first_flip['indicator']} flip: cost {abs(diff):.0f}t]"
            
            lines.append(f"  {rt['entry']['time_str']} {rt['direction']:5} {rt['pnl_ticks']:+6.0f}t (${rt['pnl_ticks'] * TICK_VALUE:+7.2f}) {pnl_marker} {sig_info}{exit_info}")
        lines.append("")
    
    # === EARLY EXIT ANALYSIS - THREE STRATEGIES COMPARED ===
    if early_exit_analysis:
        lines.append("=" * 90)
        lines.append("EXIT STRATEGY COMPARISON: SL/TP vs Confluence Drop vs Single Indicator Flip")
        lines.append("=" * 90)
        lines.append("")
        lines.append("(Exit times estimated by scanning BAR data for SL/TP price levels)")
        lines.append("")
        
        ea = early_exit_analysis
        
        # Show trades without bar data first if any
        if ea.get('trades_no_bar_data', 0) > 0:
            lines.append(f"Trades SKIPPED (no BAR data): {ea['trades_no_bar_data']} (outside CSV coverage)")
            lines.append("")
        
        # === SUMMARY TABLE ===
        lines.append("STRATEGY COMPARISON SUMMARY")
        lines.append("-" * 70)
        lines.append(f"{'Strategy':<30} {'Trades':>8} {'Better':>8} {'Worse':>8} {'NET':>12}")
        lines.append("-" * 70)
        
        # Current SL/TP (baseline)
        lines.append(f"{'1. Current SL/TP (baseline)':<30} {total_trades:>8} {'---':>8} {'---':>8} {'+0t':>12}")
        
        # Confluence drop
        conf_ea = ea.get('confluence')
        if conf_ea and conf_ea['trades_analyzed'] > 0:
            net_str = f"{conf_ea['total_difference_ticks']:+.0f}t"
            lines.append(f"{'2. Confluence drop below 6':<30} {conf_ea['trades_analyzed']:>8} {conf_ea['early_exit_better_count']:>8} {conf_ea['early_exit_worse_count']:>8} {net_str:>12}")
        else:
            lines.append(f"{'2. Confluence drop below 6':<30} {'0':>8} {'---':>8} {'---':>8} {'+0t':>12}")
        
        # Single indicator flip
        flip_ea = ea.get('flip')
        if flip_ea and flip_ea['trades_analyzed'] > 0:
            net_str = f"{flip_ea['total_difference_ticks']:+.0f}t"
            lines.append(f"{'3. Single indicator flip':<30} {flip_ea['trades_analyzed']:>8} {flip_ea['early_exit_better_count']:>8} {flip_ea['early_exit_worse_count']:>8} {net_str:>12}")
        else:
            lines.append(f"{'3. Single indicator flip':<30} {'0':>8} {'---':>8} {'---':>8} {'+0t':>12}")
        
        lines.append("-" * 70)
        lines.append("")
        
        # === DETAILED BREAKDOWN: CONFLUENCE DROP ===
        if conf_ea and conf_ea['trades_analyzed'] > 0:
            lines.append("STRATEGY 2: CONFLUENCE DROP BELOW 6")
            lines.append("-" * 40)
            lines.append(f"Trades affected: {conf_ea['trades_analyzed']} of {total_trades}")
            lines.append(f"  Losers with drop: {conf_ea['loser_count']} → savings: {conf_ea['loser_savings_ticks']:+.0f}t")
            lines.append(f"  Winners with drop: {conf_ea['winner_count']} → cost: {conf_ea['winner_cost_ticks']:+.0f}t")
            lines.append(f"  NET: {conf_ea['total_difference_ticks']:+.0f}t")
            lines.append("")
            lines.append("  Entry     Dir   Exit  Actual  @Time     Conf   Hypo    Diff   Result")
            for t in conf_ea['trade_details']:
                result = "SAVE" if t['early_exit_better'] else "COST"
                outcome = "W" if t['was_winner'] else "L"
                exit_type = t.get('estimated_exit_type', '?')[:2]
                conf_change = f"{t['entry_confluence']}→{t['exit_confluence']}"
                lines.append(
                    f"  {t['entry_time']} {t['direction']:5} {exit_type:4} {t['actual_pnl']:+5.0f}t  "
                    f"{t['trigger_time']}  {conf_change:5} {t['hypo_pnl']:+5.0f}t {t['difference']:+5.0f}t {result}({outcome})"
                )
            lines.append("")
        
        # === DETAILED BREAKDOWN: SINGLE INDICATOR FLIP ===
        if flip_ea and flip_ea['trades_analyzed'] > 0:
            lines.append("STRATEGY 3: SINGLE INDICATOR FLIP")
            lines.append("-" * 40)
            lines.append(f"Trades affected: {flip_ea['trades_analyzed']} of {total_trades}")
            lines.append(f"  Losers with flip: {flip_ea['loser_count']} → savings: {flip_ea['loser_savings_ticks']:+.0f}t")
            lines.append(f"  Winners with flip: {flip_ea['winner_count']} → cost: {flip_ea['winner_cost_ticks']:+.0f}t")
            lines.append(f"  NET: {flip_ea['total_difference_ticks']:+.0f}t")
            lines.append("")
            lines.append("  Entry     Dir   Exit  Actual  @Time     Ind   Hypo    Diff   Result")
            for t in flip_ea['trade_details']:
                result = "SAVE" if t['early_exit_better'] else "COST"
                outcome = "W" if t['was_winner'] else "L"
                exit_type = t.get('estimated_exit_type', '?')[:2]
                lines.append(
                    f"  {t['entry_time']} {t['direction']:5} {exit_type:4} {t['actual_pnl']:+5.0f}t  "
                    f"{t['trigger_time']}  {t['indicator']:5} {t['hypo_pnl']:+5.0f}t {t['difference']:+5.0f}t {result}({outcome})"
                )
            lines.append("")
    
    # === TRAILING STOP ANALYSIS ===
    if trailing_stop_analysis and trailing_stop_analysis.get('configs'):
        lines.append("=" * 90)
        lines.append("TRAILING STOP SIMULATION ANALYSIS")
        lines.append("=" * 90)
        lines.append("")
        lines.append("Simulates trailing stops that activate after reaching profit threshold,")
        lines.append("then trail behind price by specified distance. Fixed SL/TP still honored.")
        lines.append("")
        
        if trailing_stop_analysis.get('trades_no_bar_data', 0) > 0:
            lines.append(f"Trades SKIPPED (no BAR data): {trailing_stop_analysis['trades_no_bar_data']}")
            lines.append("")
        
        # Summary comparison table
        lines.append("TRAILING STOP COMPARISON SUMMARY")
        lines.append("-" * 90)
        lines.append(f"{'Strategy':<25} {'Trades':>7} {'Better':>7} {'Worse':>7} {'Same':>6} {'Actual':>10} {'Trail':>10} {'NET':>10}")
        lines.append("-" * 90)
        
        # Baseline
        baseline_pnl = sum(rt['pnl_ticks'] for rt in complete_rts)
        lines.append(f"{'Fixed SL/TP (baseline)':<25} {total_trades:>7} {'---':>7} {'---':>7} {'---':>6} {baseline_pnl:>+10.0f}t {'---':>10} {'+0t':>10}")
        
        # Each trailing config
        for config_name in sorted(trailing_stop_analysis['configs'].keys()):
            ts = trailing_stop_analysis['configs'][config_name]
            if ts['trades_analyzed'] == 0:
                continue
            
            net_str = f"{ts['total_difference_ticks']:+.0f}t"
            actual_str = f"{ts['total_actual_pnl']:+.0f}t"
            trail_str = f"{ts['total_trail_pnl']:+.0f}t"
            
            lines.append(
                f"{config_name:<25} {ts['trades_analyzed']:>7} {ts['trades_better']:>7} "
                f"{ts['trades_worse']:>7} {ts['trades_same']:>6} {actual_str:>10} {trail_str:>10} {net_str:>10}"
            )
        
        lines.append("-" * 90)
        lines.append("")
        
        # Detailed breakdown for each config
        for config_name in sorted(trailing_stop_analysis['configs'].keys()):
            ts = trailing_stop_analysis['configs'][config_name]
            if ts['trades_analyzed'] == 0:
                continue
            
            config = ts['config']
            lines.append(f"TRAILING STOP: {config_name}")
            lines.append(f"  Configuration: {config['description']}")
            lines.append(f"  - Activation threshold: +{config['activation_ticks']} ticks (+${config['activation_ticks'] * TICK_VALUE / 4:.0f})")
            lines.append(f"  - Trail distance: {config['trail_distance_ticks']} ticks (${config['trail_distance_ticks'] * TICK_VALUE / 4:.0f})")
            lines.append("-" * 50)
            
            lines.append(f"  Trades analyzed: {ts['trades_analyzed']}")
            lines.append(f"  NET impact: {ts['total_difference_ticks']:+.0f}t (${ts['total_difference_ticks'] * TICK_VALUE:+.2f})")
            lines.append("")
            
            # Exit type breakdown
            lines.append("  Exit Type Breakdown:")
            for exit_type, count in sorted(ts['trail_exits_by_type'].items()):
                pct = count / ts['trades_analyzed'] * 100 if ts['trades_analyzed'] > 0 else 0
                lines.append(f"    {exit_type}: {count} ({pct:.0f}%)")
            lines.append("")
            
            # Winners vs Losers impact
            lines.append("  Impact on Winners (actual result was profitable):")
            lines.append(f"    Analyzed: {ts['winners_analyzed']}")
            lines.append(f"    Trail better: {ts['winners_better_with_trail']}, Trail worse: {ts['winners_worse_with_trail']}")
            lines.append(f"    Winner P&L change: {ts['winners_difference']:+.0f}t")
            lines.append("")
            
            lines.append("  Impact on Losers (actual result was loss):")
            lines.append(f"    Analyzed: {ts['losers_analyzed']}")
            lines.append(f"    Trail better: {ts['losers_better_with_trail']}, Trail worse: {ts['losers_worse_with_trail']}")
            lines.append(f"    Loser P&L change: {ts['losers_difference']:+.0f}t")
            lines.append("")
            
            # Let winners run analysis
            if ts['trades_exceeded_tp'] > 0:
                avg_max = ts['max_profit_sum'] / ts['trades_analyzed'] if ts['trades_analyzed'] > 0 else 0
                lines.append(f"  'Let Winners Run' Analysis:")
                lines.append(f"    Trades where price exceeded 120t TP: {ts['trades_exceeded_tp']}")
                lines.append(f"    Average max profit reached: {avg_max:.0f}t")
                lines.append("")
            
            # Trade details (first 30)
            lines.append("  Trade Details (showing impact per trade):")
            lines.append("  Entry     Dir    Actual  Trail   Diff   Exit  MaxProf  Trail?")
            for t in ts['trade_details'][:30]:  # Limit to 30 for readability
                result = "+" if t['is_better'] else ("-" if t['difference'] < 0 else "=")
                trail_flag = "Yes" if t['trail_activated'] else "No"
                lines.append(
                    f"  {t['entry_time']} {t['direction']:5} {t['actual_pnl']:+6.0f}t {t['trail_pnl']:+6.0f}t "
                    f"{t['difference']:+5.0f}t {t['exit_type']:5} {t['max_profit_ticks']:+6.0f}t  {trail_flag}"
                )
            
            if len(ts['trade_details']) > 30:
                lines.append(f"  ... and {len(ts['trade_details']) - 30} more trades")
            lines.append("")
        
        # Recommendation
        lines.append("TRAILING STOP RECOMMENDATION")
        lines.append("-" * 30)
        best_config = None
        best_net = 0
        for config_name, ts in trailing_stop_analysis['configs'].items():
            if ts['total_difference_ticks'] > best_net:
                best_net = ts['total_difference_ticks']
                best_config = config_name
        
        if best_config and best_net > 0:
            lines.append(f"  Best trailing config: {best_config} with {best_net:+.0f}t improvement")
            lines.append(f"  Consider implementing this trailing stop strategy.")
        else:
            lines.append(f"  No trailing stop configuration improved results.")
            lines.append(f"  Current fixed SL/TP strategy performs best.")
        lines.append("")
    
    # Signal alignment section
    lines.append("SIGNAL ALIGNMENT ANALYSIS")
    lines.append("-" * 25)
    lines.append("")
    
    lines.append(f"ALIGNED with signals: {len(aligned)} trades, {aligned_pnl:+.0f}t")
    for rt in aligned:
        sig = rt['signal']
        pnl_marker = "✓" if rt['pnl_ticks'] > 0 else ""
        conf_str = f"{sig['confluence_count']}/{sig['confluence_total']}"
        lines.append(f"  {rt['entry']['time_str']} {rt['direction']:5} {rt['pnl_ticks']:+5.0f}t <- {sig['source']} @ {sig['time_str']} [{sig['trigger']}] {conf_str} {pnl_marker}")
    lines.append("")
    
    lines.append(f"COUNTER to signals: {len(counter)} trades, {counter_pnl:+.0f}t")
    for rt in counter:
        sig = rt['signal']
        conf_str = f"{sig['confluence_count']}/{sig['confluence_total']}"
        lines.append(f"  {rt['entry']['time_str']} {rt['direction']:5} {rt['pnl_ticks']:+5.0f}t <- Against {sig['direction']} @ {sig['time_str']} [{sig['trigger']}]")
    lines.append("")
    
    lines.append(f"NO SIGNAL nearby: {len(no_signal)} trades, {no_signal_pnl:+.0f}t")
    lines.append("")
    
    # Exit trigger stats section
    if adverse_flip_stats and adverse_flip_stats.get('total_trades_with_bars', 0) > 0:
        lines.append("EXIT TRIGGER OCCURRENCE (during trades)")
        lines.append("-" * 45)
        total_with_bars = adverse_flip_stats['total_trades_with_bars']
        
        # Confluence drop stats
        with_conf_drop = adverse_flip_stats['trades_with_conf_drop']
        lines.append(f"Trades with BAR data: {total_with_bars}")
        lines.append(f"  Confluence dropped below 6: {with_conf_drop} trades")
        lines.append(f"    - On losers: {adverse_flip_stats['losers_with_conf_drop']}")
        lines.append(f"    - On winners: {adverse_flip_stats['winners_with_conf_drop']}")
        
        # Indicator flip stats
        with_flip = adverse_flip_stats['trades_with_flip']
        lines.append(f"  Single indicator flipped: {with_flip} trades")
        lines.append(f"    - On losers: {adverse_flip_stats['losers_with_flip']}")
        lines.append(f"    - On winners: {adverse_flip_stats['winners_with_flip']}")
        
        # Neither triggered
        neither = total_with_bars - max(with_conf_drop, with_flip)
        lines.append(f"  Neither triggered: {total_with_bars - with_conf_drop} (conf) / {total_with_bars - with_flip} (flip)")
        lines.append("")
    
    # Indicator correlation analysis
    if indicator_stats:
        lines.append("INDICATOR STATE CORRELATION")
        lines.append("-" * 27)
        lines.append("Indicator   UP(W/L)     DN(W/L)     Notes")
        for ind in sorted(indicator_stats.keys()):
            stats = indicator_stats[ind]
            up_total = stats['up_wins'] + stats['up_losses']
            dn_total = stats['dn_wins'] + stats['dn_losses']
            up_wr = (stats['up_wins'] / up_total * 100) if up_total > 0 else 0
            dn_wr = (stats['dn_wins'] / dn_total * 100) if dn_total > 0 else 0
            
            # Notes
            notes = ""
            if up_wr > dn_wr + 10:
                notes = "Better when UP"
            elif dn_wr > up_wr + 10:
                notes = "Better when DN"
            
            lines.append(f"  {ind:6}    {stats['up_wins']}/{stats['up_losses']} ({up_wr:4.0f}%)  {stats['dn_wins']}/{stats['dn_losses']} ({dn_wr:4.0f}%)  {notes}")
        lines.append("")
    
    # Best/worst trades
    lines.append("BEST & WORST TRADES")
    lines.append("-" * 19)
    lines.append("")
    lines.append("TOP 5 WINNERS:")
    for rt in top_5:
        sig = rt.get('signal')
        if sig:
            sig_info = f"[{sig['source']}:{sig['trigger']}] {sig['confluence_count']}/{sig['confluence_total']}"
        else:
            sig_info = "[NO SIGNAL]"
        lines.append(f"  {rt['entry']['time_str']} {rt['direction']:5} {rt['pnl_ticks']:+5.0f}t {sig_info}")
    lines.append("")
    
    lines.append("BOTTOM 5 LOSERS:")
    for rt in bottom_5:
        sig = rt.get('signal')
        if sig:
            sig_info = f"[{sig['source']}:{sig['trigger']}] {sig['confluence_count']}/{sig['confluence_total']}"
        else:
            sig_info = "[NO SIGNAL]"
        lines.append(f"  {rt['entry']['time_str']} {rt['direction']:5} {rt['pnl_ticks']:+5.0f}t {sig_info}")
    lines.append("")
    
    # Time-based analysis
    lines.append("TIME-BASED ANALYSIS")
    lines.append("-" * 19)
    bucket_order = ['Pre-8:30', '8:30-9:00', '9:00-10:00', '10:00-11:00', '11:00+']
    for bucket in bucket_order:
        if bucket in time_buckets:
            stats = time_buckets[bucket]
            lines.append(f"{bucket:12}: {stats['trades']} trades, {stats['wins']}W, {stats['pnl']:+.0f}t")
    lines.append("")
    
    # Key insights
    lines.append("=" * 80)
    lines.append("KEY INSIGHTS")
    lines.append("=" * 80)
    lines.append("")
    
    # Insight 1: Signal alignment
    lines.append("1. SIGNAL ALIGNMENT:")
    if total_trades > 0:
        align_pct = len(aligned) / total_trades * 100
        lines.append(f"   - {len(aligned)} of {total_trades} trades ({align_pct:.0f}%) aligned with signals")
    lines.append("")
    
    # Insight 2: P&L by alignment
    lines.append("2. P&L BY ALIGNMENT:")
    lines.append(f"   - ALIGNED trades: {aligned_pnl:+.0f}t (${aligned_pnl * TICK_VALUE:+.2f})")
    lines.append(f"   - COUNTER trades: {counter_pnl:+.0f}t (${counter_pnl * TICK_VALUE:+.2f})")
    lines.append(f"   - NO SIGNAL trades: {no_signal_pnl:+.0f}t (${no_signal_pnl * TICK_VALUE:+.2f})")
    lines.append("")
    
    # Insight 3: Confluence effectiveness
    lines.append("3. CONFLUENCE INSIGHTS:")
    if confluence_stats:
        best_conf = max(confluence_stats.items(), key=lambda x: x[1]['pnl']) if confluence_stats else None
        if best_conf:
            lines.append(f"   - Best performing: {best_conf[0]} with {best_conf[1]['pnl']:+.0f}t")
        
        # High confluence performance
        high_conf_pnl = sum(
            stats['pnl'] for key, stats in confluence_stats.items()
            if key.split('/')[0].isdigit() and int(key.split('/')[0]) >= int(key.split('/')[1]) - 1
        )
        lines.append(f"   - High confluence (N-1 or higher): {high_conf_pnl:+.0f}t")
    lines.append("")
    
    # Insight 4: Exit strategy comparison
    if early_exit_analysis:
        lines.append("4. EXIT STRATEGY COMPARISON:")
        conf_ea = early_exit_analysis.get('confluence')
        flip_ea = early_exit_analysis.get('flip')
        
        # Confluence drop summary
        if conf_ea and conf_ea['trades_analyzed'] > 0:
            net = conf_ea['total_difference_ticks']
            verdict = "SAVE" if net > 0 else "COST"
            lines.append(f"   - Confluence drop: {verdict} {abs(net):.0f}t ({conf_ea['trades_analyzed']} trades affected)")
        else:
            lines.append(f"   - Confluence drop: no trades affected")
        
        # Single indicator flip summary
        if flip_ea and flip_ea['trades_analyzed'] > 0:
            net = flip_ea['total_difference_ticks']
            verdict = "SAVE" if net > 0 else "COST"
            lines.append(f"   - Indicator flip:  {verdict} {abs(net):.0f}t ({flip_ea['trades_analyzed']} trades affected)")
        else:
            lines.append(f"   - Indicator flip:  no trades affected")
        
        # Trailing stop summary
        if trailing_stop_analysis and trailing_stop_analysis.get('configs'):
            best_trail = None
            best_trail_net = 0
            for config_name, ts in trailing_stop_analysis['configs'].items():
                if ts['total_difference_ticks'] > best_trail_net:
                    best_trail_net = ts['total_difference_ticks']
                    best_trail = config_name
            
            if best_trail and best_trail_net > 0:
                lines.append(f"   - Trailing stop:   SAVE {best_trail_net:.0f}t (best: {best_trail})")
            else:
                lines.append(f"   - Trailing stop:   no improvement found")
        
        # Recommendation
        conf_net = conf_ea['total_difference_ticks'] if conf_ea else 0
        flip_net = flip_ea['total_difference_ticks'] if flip_ea else 0
        trail_net = best_trail_net if trailing_stop_analysis else 0
        
        if conf_net > 0 or flip_net > 0 or trail_net > 0:
            best_strategy = "Current SL/TP"
            best_improvement = 0
            if conf_net > best_improvement:
                best_improvement = conf_net
                best_strategy = "Confluence drop"
            if flip_net > best_improvement:
                best_improvement = flip_net
                best_strategy = "Indicator flip"
            if trail_net > best_improvement:
                best_improvement = trail_net
                best_strategy = f"Trailing stop ({best_trail})"
            
            if best_improvement > 0:
                lines.append(f"   → {best_strategy} would improve results by {best_improvement:+.0f}t")
        else:
            lines.append(f"   → Current SL/TP performs best")
        lines.append("")
    
    # Insight 5: Recommendations
    lines.append("5. RECOMMENDATIONS:")
    if no_signal_pnl < 0:
        lines.append(f"   - Avoiding NO SIGNAL trades would have saved {abs(no_signal_pnl):.0f}t")
    if counter_pnl < 0:
        lines.append(f"   - Avoiding COUNTER trades would have saved {abs(counter_pnl):.0f}t")
    
    # Find best trigger
    if trigger_stats:
        profitable_triggers = [(k, v) for k, v in trigger_stats.items() if v['pnl'] > 0]
        if profitable_triggers:
            best_trigger = max(profitable_triggers, key=lambda x: x[1]['pnl'])
            lines.append(f"   - Best trigger: {best_trigger[0]} with {best_trigger[1]['pnl']:+.0f}t")
    lines.append("")
    
    # === SLIPPAGE ANALYSIS ===
    # Collect slippage data from complete roundtrips
    entry_slippages = [rt.get('entry_slippage_ticks', 0) for rt in complete_rts if rt.get('entry_slippage_ticks') is not None]
    exit_slippages = [rt.get('exit_slippage_ticks', 0) for rt in complete_rts if rt.get('exit_slippage_ticks') is not None]
    
    # Group exit slippage by reason
    exit_slip_by_reason = defaultdict(list)
    for rt in complete_rts:
        reason = rt.get('exit_reason', 'UNKNOWN')
        slip = rt.get('exit_slippage_ticks', 0)
        if slip is not None:
            exit_slip_by_reason[reason].append(slip)
    
    # Only show slippage section if we have data
    if entry_slippages or exit_slippages:
        lines.append("=" * 80)
        lines.append("SLIPPAGE ANALYSIS")
        lines.append("=" * 80)
        lines.append("")
        
        # Helper for statistics
        def calc_stats(data):
            if not data:
                return {'count': 0, 'mean': 0, 'median': 0, 'std': 0, 'min': 0, 'max': 0, 'total': 0}
            n = len(data)
            total = sum(data)
            mean = total / n
            sorted_data = sorted(data)
            median = sorted_data[n // 2] if n % 2 == 1 else (sorted_data[n // 2 - 1] + sorted_data[n // 2]) / 2
            variance = sum((x - mean) ** 2 for x in data) / n if n > 0 else 0
            std = variance ** 0.5
            return {
                'count': n,
                'mean': mean,
                'median': median,
                'std': std,
                'min': min(data),
                'max': max(data),
                'total': total
            }
        
        # Entry slippage stats
        if entry_slippages:
            entry_stats = calc_stats(entry_slippages)
            lines.append("ENTRY SLIPPAGE:")
            lines.append(f"   Trades with data: {entry_stats['count']}")
            lines.append(f"   Mean:   {entry_stats['mean']:+.1f}t (${entry_stats['mean'] * TICK_VALUE:+.2f})")
            lines.append(f"   Median: {entry_stats['median']:+.1f}t")
            lines.append(f"   Std Dev: {entry_stats['std']:.1f}t")
            lines.append(f"   Range:  {entry_stats['min']:+.0f}t to {entry_stats['max']:+.0f}t")
            lines.append(f"   TOTAL:  {entry_stats['total']:+.0f}t (${entry_stats['total'] * TICK_VALUE:+.2f})")
            lines.append("")
        
        # Exit slippage stats
        if exit_slippages:
            exit_stats = calc_stats(exit_slippages)
            lines.append("EXIT SLIPPAGE (all exits):")
            lines.append(f"   Trades with data: {exit_stats['count']}")
            lines.append(f"   Mean:   {exit_stats['mean']:+.1f}t (${exit_stats['mean'] * TICK_VALUE:+.2f})")
            lines.append(f"   Median: {exit_stats['median']:+.1f}t")
            lines.append(f"   Std Dev: {exit_stats['std']:.1f}t")
            lines.append(f"   Range:  {exit_stats['min']:+.0f}t to {exit_stats['max']:+.0f}t")
            lines.append(f"   TOTAL:  {exit_stats['total']:+.0f}t (${exit_stats['total'] * TICK_VALUE:+.2f})")
            lines.append("")
        
        # Exit slippage by reason
        if exit_slip_by_reason:
            lines.append("EXIT SLIPPAGE BY REASON:")
            for reason in ['SL', 'TRAIL', 'TP', 'UNKNOWN']:
                if reason in exit_slip_by_reason:
                    reason_data = exit_slip_by_reason[reason]
                    reason_stats = calc_stats(reason_data)
                    lines.append(f"   {reason:8} ({reason_stats['count']:3} trades): Mean {reason_stats['mean']:+5.1f}t | Total {reason_stats['total']:+6.0f}t (${reason_stats['total'] * TICK_VALUE:+.2f})")
            lines.append("")
        
        # Total slippage cost summary
        total_entry_slip = sum(entry_slippages) if entry_slippages else 0
        total_exit_slip = sum(exit_slippages) if exit_slippages else 0
        total_slippage = total_entry_slip + total_exit_slip
        total_slippage_dollars = total_slippage * TICK_VALUE
        
        # Broker fee estimation ($4.50 per round-trip for NQ)
        broker_fee_per_trade = 4.50
        total_broker_fees = total_trades * broker_fee_per_trade
        
        # Adjusted P&L
        gross_pnl_dollars = total_pnl * TICK_VALUE
        net_pnl_dollars = gross_pnl_dollars - total_slippage_dollars - total_broker_fees
        
        lines.append("COST SUMMARY:")
        lines.append(f"   Gross P&L:        {total_pnl:+.0f}t (${gross_pnl_dollars:+.2f})")
        lines.append(f"   Entry Slippage:   {total_entry_slip:+.0f}t (${total_entry_slip * TICK_VALUE:+.2f})")
        lines.append(f"   Exit Slippage:    {total_exit_slip:+.0f}t (${total_exit_slip * TICK_VALUE:+.2f})")
        lines.append(f"   Total Slippage:   {total_slippage:+.0f}t (${total_slippage_dollars:+.2f})")
        lines.append(f"   Broker Fees:      {total_trades} × ${broker_fee_per_trade:.2f} = ${total_broker_fees:.2f}")
        lines.append(f"   ─────────────────────────────────────")
        lines.append(f"   NET P&L:          ${net_pnl_dollars:+.2f}")
        lines.append("")
        
        # Slippage as percentage of gross
        if gross_pnl_dollars != 0:
            slip_pct = (total_slippage_dollars / abs(gross_pnl_dollars)) * 100
            total_costs_pct = ((total_slippage_dollars + total_broker_fees) / abs(gross_pnl_dollars)) * 100
            lines.append(f"   Slippage as % of |Gross|: {slip_pct:.1f}%")
            lines.append(f"   Total costs as % of |Gross|: {total_costs_pct:.1f}%")
            lines.append("")
    
    # Multi-day comparison
    if folder_path:
        prev_analyses = find_previous_analyses(folder_path, date_str)
        
        # Filter to only include dates BEFORE the current analysis date
        prev_analyses = [a for a in prev_analyses if a['date'] < date_str]
        
        # Keep last 5 days before current date
        prev_analyses = prev_analyses[-5:]
        
        # Detect if this is a local folder for the header
        abs_folder = os.path.abspath(folder_path.rstrip('/\\'))
        folder_name = os.path.basename(abs_folder)
        is_local = folder_name.endswith('_local')
        source_label = "LOCAL" if is_local else "VPS"
        
        if len(prev_analyses) > 0:
            lines.append("=" * 80)
            lines.append(f"MULTI-DAY COMPARISON - {source_label} (Previous Days)")
            lines.append("=" * 80)
            lines.append("")
            lines.append("Date        Trades  Win%    P&L      Signal Alignment")
            lines.append("-" * 70)
            
            for stats in prev_analyses:
                dt = datetime.strptime(stats['date'], "%Y-%m-%d")
                date_display = dt.strftime("%b %d")
                
                if stats['aligned_count'] > 0 or stats['no_signal_count'] > 0:
                    align_info = f"{stats['aligned_count']} aligned ({stats['aligned_pnl']:+d}t), {stats['no_signal_count']} no signal ({stats['no_signal_pnl']:+d}t)"
                else:
                    align_info = "N/A"
                
                lines.append(f"{date_display:11} {stats['trades']:3}     {stats['win_rate']:.0f}%   {stats['pnl']:+5d}t    {align_info}")
            
            # Add current day summary line for easy comparison
            lines.append("-" * 70)
            lines.append(f"{'TODAY':11} {total_trades:3}     {win_rate:.0f}%   {int(total_pnl):+5d}t    {len(aligned)} aligned ({int(aligned_pnl):+d}t), {len(no_signal)} no signal ({int(no_signal_pnl):+d}t)")
            lines.append("")
    
    lines.append("=" * 80)
    
    return "\n".join(lines)
