#!/usr/bin/env python3
"""
AnalyzeMarketReplaySessionVPS.py
Analyzes NinjaTrader Market Replay sessions from VPS logs.
Generates {Mon}{DD}_MR_{Start}_{End}_Trading_Analysis{N}.txt report.

Usage: 
    python AnalyzeMarketReplaySessionVPS.py <start_date> <end_date> [--trader-log <path>] [--csv-log <path>]

Examples:
    python AnalyzeMarketReplaySessionVPS.py 2025-12-19 2025-12-31
    python AnalyzeMarketReplaySessionVPS.py 2025-12-19 2025-12-31 --trader-log "path/to/ActiveNikiTrader_*.txt"
"""

import sys
import os
import re
import glob
from datetime import datetime
from collections import defaultdict

# === CONFIGURATION ===
VPS_LOG_PATH = r"C:\Users\Administrator\Documents\NinjaTrader 8\log"
OUTPUT_BASE_PATH = r"C:\Users\Administrator\Downloads\ActiveNiki"
TICK_VALUE = 5.00  # NQ tick value in dollars
TICK_SIZE = 0.25   # NQ tick size


def find_latest_file(pattern, folder):
    """Find the most recently modified file matching pattern."""
    search_path = os.path.join(folder, pattern)
    files = glob.glob(search_path)
    if not files:
        return None
    return max(files, key=os.path.getmtime)


def parse_header_config(lines):
    """Parse strategy configuration from log header."""
    config = {
        'min_confluence_signal': 4,
        'total_indicators': 8,
        'min_confluence_trade': 5,
        'cooldown': 10,
        'stop_loss': 160,
        'take_profit': 600,
        'auto_trade': False,
        'trading_hours': '',
        'daily_loss_limit': 0,
        'daily_profit_target': 0
    }
    
    for line in lines[:50]:  # Check first 50 lines for config
        # Signal Filter: MinConf=4/8, MaxBars=3, Cooldown=10
        match = re.search(r'Signal Filter: MinConf=(\d+)/(\d+)', line)
        if match:
            config['min_confluence_signal'] = int(match.group(1))
            config['total_indicators'] = int(match.group(2))
        
        # Auto Trade: ON | MinConf for Trade=5/8
        match = re.search(r'Auto Trade: (ON|OFF)', line)
        if match:
            config['auto_trade'] = match.group(1) == 'ON'
        
        match = re.search(r'MinConf for Trade=(\d+)', line)
        if match:
            config['min_confluence_trade'] = int(match.group(1))
        
        # Risk: SL=$160, TP=$600
        match = re.search(r'SL=\$(\d+)', line)
        if match:
            config['stop_loss'] = int(match.group(1))
        match = re.search(r'TP=\$(\d+)', line)
        if match:
            config['take_profit'] = int(match.group(1))
        
        # Trading Hours
        match = re.search(r'Trading Hours: (.+)', line)
        if match:
            config['trading_hours'] = match.group(1).strip()
        
        # Daily Loss Limit
        match = re.search(r'Daily Loss Limit: \$(\d+)', line)
        if match:
            config['daily_loss_limit'] = int(match.group(1))
        
        # Daily Profit Target
        match = re.search(r'Daily Profit Target: \$(\d+)', line)
        if match:
            config['daily_profit_target'] = int(match.group(1))
    
    return config


def parse_signals(lines):
    """Parse signals from ActiveNikiTrader log."""
    signals = []
    
    i = 0
    while i < len(lines):
        line = lines[i]
        
        # Look for signal box: *** LONG SIGNAL @ 09:32:34 ***
        signal_match = re.search(r'\*\*\* (LONG|SHORT) SIGNAL @ (\d{2}:\d{2}:\d{2}) \*\*\*', line)
        if signal_match:
            direction = signal_match.group(1)
            time_str = signal_match.group(2)
            
            # Parse following lines for details
            trigger = ''
            confluence_count = 0
            confluence_total = 8
            indicator_states = {}
            ask_price = 0
            bid_price = 0
            order_placed = False
            blocked_reason = None
            
            for j in range(1, 20):
                if i + j >= len(lines):
                    break
                next_line = lines[i + j]
                
                # End of signal box
                if '╚' in next_line:
                    # Check lines after box for order status
                    for k in range(j + 1, j + 5):
                        if i + k < len(lines):
                            status_line = lines[i + k]
                            if '>>> ORDER PLACED:' in status_line:
                                order_placed = True
                                break
                            elif '>>> OUTSIDE TRADING HOURS:' in status_line:
                                blocked_reason = 'OUTSIDE_HOURS'
                                break
                            elif 'BLOCKED by cooldown' in status_line:
                                blocked_reason = 'COOLDOWN'
                                break
                            elif '>>> SIGNAL ONLY' in status_line:
                                blocked_reason = 'BELOW_TRADE_THRESHOLD'
                                break
                            elif '╔' in status_line:
                                break
                    break
                
                # Trigger line
                trigger_match = re.search(r'Trigger: (.+)', next_line)
                if trigger_match:
                    trigger = trigger_match.group(1).strip()
                
                # Ask/Bid prices
                ask_match = re.search(r'Ask: (\d+\.?\d*)', next_line)
                if ask_match:
                    ask_price = float(ask_match.group(1))
                bid_match = re.search(r'Bid: (\d+\.?\d*)', next_line)
                if bid_match:
                    bid_price = float(bid_match.group(1))
                
                # Confluence line
                conf_match = re.search(r'Confluence: (\d+)/(\d+)', next_line)
                if conf_match:
                    confluence_count = int(conf_match.group(1))
                    confluence_total = int(conf_match.group(2))
                
                # Indicator state line
                if 'RR=' in next_line and 'DT=' in next_line and 'AIQ1=' not in next_line:
                    indicator_states = parse_indicator_state(next_line)
            
            price = ask_price if direction == 'LONG' else bid_price
            
            signals.append({
                'time_str': time_str,
                'direction': direction,
                'trigger': trigger,
                'price': price,
                'confluence_count': confluence_count,
                'confluence_total': confluence_total,
                'indicators': indicator_states,
                'order_placed': order_placed,
                'blocked_reason': blocked_reason
            })
        
        i += 1
    
    return signals


def parse_indicator_state(line):
    """Parse indicator state string."""
    states = {}
    for match in re.finditer(r'(\w+)=(\w+|-?\d+)', line):
        name = match.group(1)
        value = match.group(2)
        
        if value == 'UP':
            states[name] = 'UP'
        elif value == 'DN':
            states[name] = 'DN'
        elif value.lstrip('-').isdigit():
            states[name] = 'UP' if int(value) > 0 else 'DN'
        else:
            states[name] = value
    
    return states


def parse_trades(lines):
    """Parse trades from ActiveNikiTrader log."""
    trades = []
    
    for line in lines:
        # ORDER PLACED: LONG @ Market
        order_match = re.search(r'>>> ORDER PLACED: (LONG|SHORT) @ Market', line)
        if order_match:
            # Extract log timestamp
            time_match = re.match(r'(\d{2}:\d{2}:\d{2})', line)
            log_time = time_match.group(1) if time_match else '00:00:00'
            
            trades.append({
                'type': 'ENTRY',
                'direction': order_match.group(1),
                'log_time': log_time
            })
        
        # TRADE CLOSED: P&L $-340.00 | Daily P&L: $-340.00 (1 trades)
        closed_match = re.search(r'TRADE CLOSED: P&L \$([+-]?\d+\.?\d*)', line)
        if closed_match:
            pnl = float(closed_match.group(1))
            
            time_match = re.match(r'(\d{2}:\d{2}:\d{2})', line)
            log_time = time_match.group(1) if time_match else '00:00:00'
            
            trades.append({
                'type': 'EXIT',
                'pnl_dollars': pnl,
                'pnl_ticks': pnl / TICK_VALUE,
                'log_time': log_time
            })
    
    return trades


def parse_bar_data(lines):
    """Parse BAR log lines for indicator analysis. Deduplicate by taking last entry per bar."""
    bar_dict = {}
    
    for line in lines:
        # [BAR 3127] 00:09:00 | O=25642.25 H=25642.25 L=25641.25 C=25641.50 | AIQ1=DN RR=DN DT=3 VY=UP ET=UP SW=25 T3P=UP AAA=UP SB=UP Bull=7 Bear=1
        bar_match = re.search(r'\[BAR (\d+)\] (\d{2}:\d{2}:\d{2}) \| O=(\d+\.?\d*) H=(\d+\.?\d*) L=(\d+\.?\d*) C=(\d+\.?\d*) \| (.+)', line)
        if bar_match:
            bar_num = int(bar_match.group(1))
            bar_time = bar_match.group(2)
            open_p = float(bar_match.group(3))
            high_p = float(bar_match.group(4))
            low_p = float(bar_match.group(5))
            close_p = float(bar_match.group(6))
            indicator_str = bar_match.group(7)
            
            # Parse indicator states
            indicators = parse_indicator_state(indicator_str)
            
            # Extract Bull/Bear counts
            bull_match = re.search(r'Bull=(\d+)', indicator_str)
            bear_match = re.search(r'Bear=(\d+)', indicator_str)
            bull_conf = int(bull_match.group(1)) if bull_match else 0
            bear_conf = int(bear_match.group(1)) if bear_match else 0
            
            # Store/overwrite - last entry for each bar wins
            bar_dict[bar_num] = {
                'bar_num': bar_num,
                'time': bar_time,
                'open': open_p,
                'high': high_p,
                'low': low_p,
                'close': close_p,
                'indicators': indicators,
                'bull_conf': bull_conf,
                'bear_conf': bear_conf
            }
    
    # Return sorted by bar number
    return [bar_dict[k] for k in sorted(bar_dict.keys())]


def parse_daily_events(lines):
    """Parse daily P&L resets and limits."""
    events = []
    
    for line in lines:
        # NEW DAY: Resetting Daily P&L
        if 'NEW DAY:' in line:
            events.append({'type': 'NEW_DAY', 'line': line})
        
        # DAILY LOSS LIMIT HIT
        if 'DAILY LOSS LIMIT HIT' in line:
            events.append({'type': 'LOSS_LIMIT', 'line': line})
        
        # DAILY PROFIT TARGET HIT
        if 'DAILY PROFIT TARGET' in line and 'HIT' in line:
            events.append({'type': 'PROFIT_TARGET', 'line': line})
    
    return events


def build_trade_roundtrips(trades):
    """Build round-trips from entry/exit sequence."""
    roundtrips = []
    pending_entry = None
    
    for trade in trades:
        if trade['type'] == 'ENTRY':
            pending_entry = trade
        elif trade['type'] == 'EXIT' and pending_entry:
            roundtrips.append({
                'direction': pending_entry['direction'],
                'entry_time': pending_entry['log_time'],
                'exit_time': trade['log_time'],
                'pnl_dollars': trade['pnl_dollars'],
                'pnl_ticks': trade['pnl_ticks']
            })
            pending_entry = None
    
    return roundtrips


def analyze_confluence_performance(signals, roundtrips):
    """Analyze performance by confluence level."""
    # Match signals that resulted in trades
    traded_signals = [s for s in signals if s['order_placed']]
    
    by_confluence = defaultdict(lambda: {'count': 0, 'wins': 0, 'pnl': 0})
    
    for i, sig in enumerate(traded_signals):
        if i < len(roundtrips):
            rt = roundtrips[i]
            conf_key = f"{sig['confluence_count']}/{sig['confluence_total']}"
            by_confluence[conf_key]['count'] += 1
            by_confluence[conf_key]['pnl'] += rt['pnl_ticks']
            if rt['pnl_ticks'] > 0:
                by_confluence[conf_key]['wins'] += 1
    
    return dict(by_confluence)


def analyze_trigger_performance(signals, roundtrips):
    """Analyze performance by trigger type."""
    traded_signals = [s for s in signals if s['order_placed']]
    
    by_trigger = defaultdict(lambda: {'count': 0, 'wins': 0, 'pnl': 0})
    
    for i, sig in enumerate(traded_signals):
        if i < len(roundtrips):
            rt = roundtrips[i]
            trigger = sig.get('trigger', 'UNKNOWN')
            by_trigger[trigger]['count'] += 1
            by_trigger[trigger]['pnl'] += rt['pnl_ticks']
            if rt['pnl_ticks'] > 0:
                by_trigger[trigger]['wins'] += 1
    
    return dict(by_trigger)


def analyze_direction_performance(roundtrips):
    """Analyze performance by direction."""
    by_direction = {'LONG': {'count': 0, 'wins': 0, 'pnl': 0}, 'SHORT': {'count': 0, 'wins': 0, 'pnl': 0}}
    
    for rt in roundtrips:
        direction = rt['direction']
        by_direction[direction]['count'] += 1
        by_direction[direction]['pnl'] += rt['pnl_ticks']
        if rt['pnl_ticks'] > 0:
            by_direction[direction]['wins'] += 1
    
    return by_direction


def analyze_indicator_at_signals(signals):
    """Analyze indicator states at signal times."""
    indicator_stats = defaultdict(lambda: {'up_count': 0, 'dn_count': 0})
    
    for sig in signals:
        for ind, state in sig.get('indicators', {}).items():
            if state == 'UP':
                indicator_stats[ind]['up_count'] += 1
            elif state == 'DN':
                indicator_stats[ind]['dn_count'] += 1
    
    return dict(indicator_stats)


def find_previous_runs(output_folder, start_date, end_date):
    """Find previous analysis files for the same date range."""
    parent_dir = os.path.dirname(output_folder.rstrip('/\\'))
    runs = []
    
    if not parent_dir or not os.path.exists(parent_dir):
        return runs
    
    # Pattern: *_MR_{start}_{end}_Trading_Analysis*.txt
    pattern = f"*_MR_{start_date}_{end_date}_Trading_Analysis*.txt"
    
    # Search in all dated folders
    for item in os.listdir(parent_dir):
        item_path = os.path.join(parent_dir, item)
        if not os.path.isdir(item_path):
            continue
        
        # Look for matching analysis files
        for f in glob.glob(os.path.join(item_path, pattern)):
            stats = parse_previous_run(f)
            if stats:
                runs.append(stats)
    
    # Sort by file modification time
    runs.sort(key=lambda x: x.get('mtime', 0))
    
    return runs


def parse_previous_run(filepath):
    """Extract stats from a previous run analysis file."""
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
        
        stats = {'filepath': filepath, 'mtime': os.path.getmtime(filepath)}
        
        # Extract filename for display
        stats['filename'] = os.path.basename(filepath)
        
        # Extract run date from folder name
        folder_name = os.path.basename(os.path.dirname(filepath))
        date_match = re.match(r'(\d{4}-\d{2}-\d{2})', folder_name)
        stats['run_date'] = date_match.group(1) if date_match else 'Unknown'
        
        # Total trades
        match = re.search(r'Total Trades: (\d+)', content)
        stats['trades'] = int(match.group(1)) if match else 0
        
        # Win rate
        match = re.search(r'(\d+\.?\d*)% win rate', content)
        stats['win_rate'] = float(match.group(1)) if match else 0
        
        # Total P&L
        match = re.search(r'Total P&L: ([+-]?\d+\.?\d*) ticks', content)
        stats['pnl'] = float(match.group(1)) if match else 0
        
        return stats
    except Exception:
        return None


def get_next_run_number(output_folder, start_date, end_date, month_abbr, day):
    """Determine the next run number for the output filename."""
    pattern = f"{month_abbr}{day:02d}_MR_{start_date}_{end_date}_Trading_Analysis*.txt"
    existing = glob.glob(os.path.join(output_folder, pattern))
    
    if not existing:
        return 1
    
    # Find highest existing number
    max_num = 0
    for f in existing:
        match = re.search(r'_Trading_Analysis(\d+)\.txt$', f)
        if match:
            max_num = max(max_num, int(match.group(1)))
    
    return max_num + 1


def generate_report(config, signals, roundtrips, bars, daily_events, start_date, end_date):
    """Generate the Market Replay analysis report."""
    
    # Basic stats
    total_trades = len(roundtrips)
    wins = sum(1 for rt in roundtrips if rt['pnl_ticks'] > 0)
    losses = sum(1 for rt in roundtrips if rt['pnl_ticks'] < 0)
    win_rate = (wins / total_trades * 100) if total_trades > 0 else 0
    
    total_pnl_ticks = sum(rt['pnl_ticks'] for rt in roundtrips)
    total_pnl_dollars = sum(rt['pnl_dollars'] for rt in roundtrips)
    
    # Direction stats
    direction_stats = analyze_direction_performance(roundtrips)
    
    # Confluence stats
    confluence_stats = analyze_confluence_performance(signals, roundtrips)
    
    # Trigger stats
    trigger_stats = analyze_trigger_performance(signals, roundtrips)
    
    # Signal stats
    total_signals = len(signals)
    orders_placed = sum(1 for s in signals if s['order_placed'])
    outside_hours = sum(1 for s in signals if s['blocked_reason'] == 'OUTSIDE_HOURS')
    cooldown_blocked = sum(1 for s in signals if s['blocked_reason'] == 'COOLDOWN')
    below_threshold = sum(1 for s in signals if s['blocked_reason'] == 'BELOW_TRADE_THRESHOLD')
    
    # Best/worst trades
    sorted_trades = sorted(roundtrips, key=lambda x: x['pnl_ticks'], reverse=True)
    top_5 = sorted_trades[:5] if len(sorted_trades) >= 5 else sorted_trades
    bottom_5 = sorted_trades[-5:] if len(sorted_trades) >= 5 else sorted_trades
    
    # Build report
    lines = []
    lines.append("=" * 80)
    lines.append(f"MARKET REPLAY SESSION ANALYSIS (VPS)")
    lines.append(f"Replay Period: {start_date} to {end_date}")
    lines.append(f"Analysis Date: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    lines.append("=" * 80)
    lines.append("")
    
    # Strategy configuration
    lines.append("STRATEGY CONFIGURATION")
    lines.append("-" * 22)
    lines.append(f"Auto Trade: {'ON' if config['auto_trade'] else 'OFF'}")
    lines.append(f"Signal Threshold: {config['min_confluence_signal']}/{config['total_indicators']}")
    lines.append(f"Trade Threshold:  {config['min_confluence_trade']}/{config['total_indicators']}")
    lines.append(f"Stop Loss: ${config['stop_loss']}  |  Take Profit: ${config['take_profit']}")
    lines.append(f"Trading Hours: {config['trading_hours']}")
    if config['daily_loss_limit'] > 0:
        lines.append(f"Daily Loss Limit: ${config['daily_loss_limit']}")
    if config['daily_profit_target'] > 0:
        lines.append(f"Daily Profit Target: ${config['daily_profit_target']}")
    lines.append("")
    
    # Signal summary
    lines.append("SIGNAL SUMMARY")
    lines.append("-" * 14)
    lines.append(f"Total Signals: {total_signals}")
    lines.append(f"  Orders Placed:       {orders_placed}")
    lines.append(f"  Outside Hours:       {outside_hours}")
    lines.append(f"  Blocked by Cooldown: {cooldown_blocked}")
    lines.append(f"  Below Trade Thresh:  {below_threshold}")
    lines.append("")
    
    # Trading performance
    lines.append("TRADING PERFORMANCE")
    lines.append("-" * 19)
    lines.append(f"Total Trades: {total_trades}")
    lines.append(f"Win/Loss: {wins}W / {losses}L ({win_rate:.1f}% win rate)")
    lines.append(f"Total P&L: {total_pnl_ticks:+.0f} ticks (${total_pnl_dollars:+.2f})")
    lines.append(f"  LONG:  {direction_stats['LONG']['count']} trades, {direction_stats['LONG']['pnl']:+.0f}t")
    lines.append(f"  SHORT: {direction_stats['SHORT']['count']} trades, {direction_stats['SHORT']['pnl']:+.0f}t")
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
    
    # Trigger analysis
    if trigger_stats:
        lines.append("TRIGGER TYPE ANALYSIS")
        lines.append("-" * 21)
        for trigger, stats in sorted(trigger_stats.items(), key=lambda x: x[1]['pnl'], reverse=True):
            wr = (stats['wins'] / stats['count'] * 100) if stats['count'] > 0 else 0
            lines.append(f"  {trigger}: {stats['count']} trades, {stats['wins']}W ({wr:.0f}%), {stats['pnl']:+.0f}t")
        lines.append("")
    
    # Best/worst trades
    lines.append("BEST & WORST TRADES")
    lines.append("-" * 19)
    lines.append("TOP 5 WINNERS:")
    for rt in top_5:
        if rt['pnl_ticks'] > 0:
            lines.append(f"  {rt['entry_time']} {rt['direction']:5} {rt['pnl_ticks']:+.0f}t (${rt['pnl_dollars']:+.2f})")
    lines.append("")
    lines.append("BOTTOM 5 LOSERS:")
    for rt in reversed(bottom_5):
        if rt['pnl_ticks'] < 0:
            lines.append(f"  {rt['entry_time']} {rt['direction']:5} {rt['pnl_ticks']:+.0f}t (${rt['pnl_dollars']:+.2f})")
    lines.append("")
    
    # Daily events
    if daily_events:
        lines.append("SESSION EVENTS")
        lines.append("-" * 14)
        new_days = sum(1 for e in daily_events if e['type'] == 'NEW_DAY')
        loss_limits = sum(1 for e in daily_events if e['type'] == 'LOSS_LIMIT')
        profit_targets = sum(1 for e in daily_events if e['type'] == 'PROFIT_TARGET')
        lines.append(f"  Trading Days: {new_days}")
        if loss_limits > 0:
            lines.append(f"  Daily Loss Limit Hit: {loss_limits} time(s)")
        if profit_targets > 0:
            lines.append(f"  Daily Profit Target Hit: {profit_targets} time(s)")
        lines.append("")
    
    # Bar data summary (if available)
    if bars:
        lines.append("BAR DATA SUMMARY")
        lines.append("-" * 16)
        lines.append(f"Total Bars Logged: {len(bars)}")
        
        # Confluence distribution
        bull_counts = defaultdict(int)
        bear_counts = defaultdict(int)
        for bar in bars:
            bull_counts[bar['bull_conf']] += 1
            bear_counts[bar['bear_conf']] += 1
        
        lines.append("Bull Confluence Distribution:")
        for conf in sorted(bull_counts.keys(), reverse=True):
            if bull_counts[conf] > len(bars) * 0.01:  # Only show > 1%
                pct = bull_counts[conf] / len(bars) * 100
                lines.append(f"  {conf}/8: {bull_counts[conf]} bars ({pct:.1f}%)")
        lines.append("")
    
    # Key insights
    lines.append("=" * 80)
    lines.append("KEY INSIGHTS")
    lines.append("=" * 80)
    lines.append("")
    
    # Insight 1: Overall performance
    lines.append("1. OVERALL PERFORMANCE:")
    if total_pnl_dollars > 0:
        lines.append(f"   - Profitable session: ${total_pnl_dollars:+.2f}")
    else:
        lines.append(f"   - Losing session: ${total_pnl_dollars:+.2f}")
    lines.append(f"   - Win rate: {win_rate:.1f}%")
    lines.append("")
    
    # Insight 2: Best confluence
    if confluence_stats:
        best_conf = max(confluence_stats.items(), key=lambda x: x[1]['pnl']) if confluence_stats else None
        if best_conf:
            lines.append("2. CONFLUENCE INSIGHTS:")
            lines.append(f"   - Best performing: {best_conf[0]} with {best_conf[1]['pnl']:+.0f}t")
    lines.append("")
    
    # Insight 3: Direction bias
    lines.append("3. DIRECTION BIAS:")
    long_pnl = direction_stats['LONG']['pnl']
    short_pnl = direction_stats['SHORT']['pnl']
    better_dir = "LONG" if long_pnl > short_pnl else "SHORT"
    lines.append(f"   - {better_dir} performed better ({direction_stats[better_dir]['pnl']:+.0f}t)")
    lines.append("")
    
    # Insight 4: Signal efficiency
    if total_signals > 0:
        lines.append("4. SIGNAL EFFICIENCY:")
        efficiency = orders_placed / total_signals * 100
        lines.append(f"   - {orders_placed} of {total_signals} signals resulted in trades ({efficiency:.0f}%)")
        if outside_hours > 0:
            lines.append(f"   - {outside_hours} signals blocked by trading hours")
    lines.append("")
    
    lines.append("=" * 80)
    
    return "\n".join(lines)


def add_multi_run_comparison(report_lines, output_folder, start_date, end_date, current_stats):
    """Add MULTI-RUN COMPARISON section to report."""
    prev_runs = find_previous_runs(output_folder, start_date, end_date)
    
    if not prev_runs:
        return report_lines
    
    lines = report_lines.split('\n')
    
    # Insert before final separator
    insert_lines = []
    insert_lines.append("")
    insert_lines.append("=" * 80)
    insert_lines.append(f"MULTI-RUN COMPARISON (Previous Runs for {start_date} to {end_date})")
    insert_lines.append("=" * 80)
    insert_lines.append("")
    insert_lines.append("Run Date    Trades  Win%    P&L      File")
    insert_lines.append("-" * 70)
    
    for run in prev_runs[-5:]:  # Last 5 runs
        insert_lines.append(f"{run['run_date']:11} {run['trades']:3}     {run['win_rate']:.0f}%   {run['pnl']:+6.0f}t    {run['filename']}")
    
    insert_lines.append("-" * 70)
    insert_lines.append(f"{'THIS RUN':11} {current_stats['trades']:3}     {current_stats['win_rate']:.0f}%   {current_stats['pnl']:+6.0f}t")
    insert_lines.append("")
    
    # Insert before last line
    lines = lines[:-1] + insert_lines + [lines[-1]]
    
    return '\n'.join(lines)


def main():
    if len(sys.argv) < 3:
        print("Usage: python AnalyzeMarketReplaySessionVPS.py <start_date> <end_date> [--trader-log <path>] [--csv-log <path>]")
        print("Example: python AnalyzeMarketReplaySessionVPS.py 2025-12-19 2025-12-31")
        sys.exit(1)
    
    start_date = sys.argv[1]
    end_date = sys.argv[2]
    
    # Parse optional arguments
    trader_log_path = None
    csv_log_path = None
    
    i = 3
    while i < len(sys.argv):
        if sys.argv[i] == '--trader-log' and i + 1 < len(sys.argv):
            trader_log_path = sys.argv[i + 1]
            i += 2
        elif sys.argv[i] == '--csv-log' and i + 1 < len(sys.argv):
            csv_log_path = sys.argv[i + 1]
            i += 2
        else:
            i += 1
    
    # Find log files
    if trader_log_path is None:
        trader_log_path = find_latest_file("ActiveNikiTrader_*.txt", VPS_LOG_PATH)
    if csv_log_path is None:
        csv_log_path = find_latest_file("IndicatorValues_*.csv", VPS_LOG_PATH)
    
    if not trader_log_path or not os.path.exists(trader_log_path):
        print(f"ERROR: ActiveNikiTrader log not found in {VPS_LOG_PATH}")
        sys.exit(1)
    
    print(f"Market Replay Analysis (VPS)")
    print(f"Period: {start_date} to {end_date}")
    print(f"Trader Log: {os.path.basename(trader_log_path)}")
    if csv_log_path and os.path.exists(csv_log_path):
        print(f"CSV Log: {os.path.basename(csv_log_path)}")
    print("")
    
    # Read and parse trader log
    print("Parsing ActiveNikiTrader log...")
    with open(trader_log_path, 'r', encoding='utf-8') as f:
        trader_lines = f.readlines()
    
    config = parse_header_config(trader_lines)
    signals = parse_signals(trader_lines)
    trades = parse_trades(trader_lines)
    bars = parse_bar_data(trader_lines)
    daily_events = parse_daily_events(trader_lines)
    
    print(f"  Config: AutoTrade={'ON' if config['auto_trade'] else 'OFF'}, Signal≥{config['min_confluence_signal']}, Trade≥{config['min_confluence_trade']}")
    print(f"  Signals: {len(signals)}")
    print(f"  Trade events: {len(trades)}")
    print(f"  BAR entries: {len(bars)}")
    
    # Build round-trips
    roundtrips = build_trade_roundtrips(trades)
    print(f"  Complete trades: {len(roundtrips)}")
    
    # Create output folder (today's date)
    today = datetime.now()
    today_str = today.strftime("%Y-%m-%d")
    output_folder = os.path.join(OUTPUT_BASE_PATH, today_str)
    
    if not os.path.exists(output_folder):
        os.makedirs(output_folder)
        print(f"  Created folder: {output_folder}")
    
    # Generate report
    print("\nGenerating analysis report...")
    report = generate_report(config, signals, roundtrips, bars, daily_events, start_date, end_date)
    
    # Calculate current stats for comparison
    total_trades = len(roundtrips)
    wins = sum(1 for rt in roundtrips if rt['pnl_ticks'] > 0)
    win_rate = (wins / total_trades * 100) if total_trades > 0 else 0
    total_pnl = sum(rt['pnl_ticks'] for rt in roundtrips)
    
    current_stats = {
        'trades': total_trades,
        'win_rate': win_rate,
        'pnl': total_pnl
    }
    
    # Add multi-run comparison
    report = add_multi_run_comparison(report, output_folder, start_date, end_date, current_stats)
    
    # Determine output filename
    month_abbr = today.strftime("%b")
    day = today.day
    run_number = get_next_run_number(output_folder, start_date, end_date, month_abbr, day)
    
    output_filename = f"{month_abbr}{day:02d}_MR_{start_date}_{end_date}_Trading_Analysis{run_number}.txt"
    output_path = os.path.join(output_folder, output_filename)
    
    with open(output_path, 'w', encoding='utf-8') as f:
        f.write(report)
    
    print(f"\nAnalysis saved to: {output_path}")
    print(f"File size: {os.path.getsize(output_path)} bytes")
    
    # Check for previous runs
    prev_runs = find_previous_runs(output_folder, start_date, end_date)
    if len(prev_runs) > 1:  # More than just current run
        print(f"\nPrevious runs for {start_date} to {end_date}: {len(prev_runs) - 1}")


if __name__ == '__main__':
    main()
