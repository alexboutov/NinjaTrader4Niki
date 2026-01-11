#!/usr/bin/env python3
"""
Analyze-TradingSession.py
Analyzes NinjaTrader trading logs and ActiveNiki signal logs.
Supports both ActiveNikiMonitor (6-indicator) and ActiveNikiTrader (8-indicator) formats.
Generates Dec{DD}_Trading_Analysis.txt report.

Usage: python Analyze-TradingSession.py <folder_path> [--date YYYY-MM-DD]
"""

import sys
import os
import re
import glob
from datetime import datetime, timedelta
from collections import defaultdict

# === CONFIGURATION ===
SIGNAL_WINDOW_SECONDS = 120  # Match trades within 2 minutes of signal
TICK_VALUE = 5.00  # NQ tick value in dollars
TICK_SIZE = 0.25   # NQ tick size

# All possible indicators (superset)
ALL_INDICATORS = ['AAA', 'SB', 'DT', 'ET', 'RR', 'SW', 'T3P', 'VY']


def parse_trades(filepath):
    """Parse trades_final.txt into list of trade dicts."""
    trades = []
    if not os.path.exists(filepath):
        return trades
    
    with open(filepath, 'r', encoding='utf-8') as f:
        for line in f:
            line = line.strip()
            if not line or "New state='Filled'" not in line:
                continue
            
            # Parse timestamp: 2025-12-19 08:07:46:809
            ts_match = re.match(r'(\d{4}-\d{2}-\d{2}) (\d{2}:\d{2}:\d{2})', line)
            if not ts_match:
                continue
            
            date_str = ts_match.group(1)
            time_str = ts_match.group(2)
            timestamp = datetime.strptime(f"{date_str} {time_str}", "%Y-%m-%d %H:%M:%S")
            
            # Parse action: Buy, Sell, Buy to cover
            action_match = re.search(r"Action='([^']+)'", line)
            action = action_match.group(1) if action_match else ""
            
            # Parse fill price
            price_match = re.search(r"Fill price=(\d+\.?\d*)", line)
            fill_price = float(price_match.group(1)) if price_match else 0
            
            # Parse if it's a close order
            is_close = "Name='Close'" in line
            
            # Determine direction
            if action in ['Buy', 'Buy to cover']:
                direction = 'LONG' if not is_close else 'COVER'
            else:  # Sell
                direction = 'SHORT' if not is_close else 'CLOSE'
            
            trades.append({
                'timestamp': timestamp,
                'time_str': time_str,
                'action': action,
                'direction': direction,
                'price': fill_price,
                'is_close': is_close,
                'raw': line
            })
    
    return trades


def parse_indicator_state(indicator_str):
    """
    Parse indicator state string like 'RR=UP DT=1 VY=UP ET=UP SW=2 T3P=UP AAA=DN SB=DN'
    Returns dict of indicator -> state (UP/DN/numeric)
    """
    states = {}
    # Match patterns like RR=UP, DT=1, SW=-2
    for match in re.finditer(r'(\w+)=(\w+|-?\d+)', indicator_str):
        name = match.group(1)
        value = match.group(2)
        
        # Normalize to UP/DN
        if value == 'UP':
            states[name] = 'UP'
        elif value == 'DN':
            states[name] = 'DN'
        elif value.lstrip('-').isdigit():
            # Numeric: positive = UP, negative/zero = DN
            states[name] = 'UP' if int(value) > 0 else 'DN'
        else:
            states[name] = value
    
    return states


def parse_monitor_signals(filepath, date_str):
    """
    Parse ActiveNikiMonitor log file.
    Format: *** SIGNAL: LONG @ 08:21:05 [RR_FLIP] ***
            Price: 25747.50 | Confluence: 5/6
            RR=UP DT=1 VY=UP ET=UP SW=-1 T3P=UP
    """
    signals = []
    if not os.path.exists(filepath):
        return signals
    
    with open(filepath, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    i = 0
    while i < len(lines):
        line = lines[i].strip()
        
        # Look for signal line
        signal_match = re.search(r'\*\*\* SIGNAL: (LONG|SHORT) @ (\d{2}:\d{2}:\d{2}) \[([^\]]+)\]', line)
        if signal_match:
            direction = signal_match.group(1)
            time_str = signal_match.group(2)
            trigger = signal_match.group(3)
            
            # Parse following lines for price, confluence, indicators
            price = 0
            confluence_count = 0
            confluence_total = 6  # Default for Monitor
            indicator_states = {}
            
            # Look at next 2-3 lines
            for j in range(1, 4):
                if i + j < len(lines):
                    next_line = lines[i + j].strip()
                    
                    # Price and confluence line
                    price_match = re.search(r'Price: (\d+\.?\d*)', next_line)
                    if price_match:
                        price = float(price_match.group(1))
                    
                    conf_match = re.search(r'Confluence: (\d+)/(\d+)', next_line)
                    if conf_match:
                        confluence_count = int(conf_match.group(1))
                        confluence_total = int(conf_match.group(2))
                    
                    # Indicator state line
                    if 'RR=' in next_line and 'DT=' in next_line:
                        indicator_states = parse_indicator_state(next_line)
            
            signals.append({
                'source': 'Monitor',
                'time_str': time_str,
                'timestamp': datetime.strptime(f"{date_str} {time_str}", "%Y-%m-%d %H:%M:%S"),
                'direction': direction,
                'trigger': trigger,
                'price': price,
                'confluence_count': confluence_count,
                'confluence_total': confluence_total,
                'indicators': indicator_states,
                'order_placed': False,  # Monitor doesn't place orders
                'blocked_reason': None
            })
        
        i += 1
    
    return signals


def parse_trader_signals(filepath, date_str):
    """
    Parse ActiveNikiTrader log file.
    Format: ║  *** LONG SIGNAL @ 09:32:34 ***
            ║  Trigger: YellowSquare+RR
            ║  Confluence: 4/5
            ║  RR=UP DT=1 VY=UP ET=UP SW=2 T3P=UP AAA=DN SB=DN
    """
    signals = []
    if not os.path.exists(filepath):
        return signals
    
    with open(filepath, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    i = 0
    while i < len(lines):
        line = lines[i].strip()
        
        # Look for signal line (box format)
        signal_match = re.search(r'\*\*\* (LONG|SHORT) SIGNAL @ (\d{2}:\d{2}:\d{2}) \*\*\*', line)
        if signal_match:
            direction = signal_match.group(1)
            time_str = signal_match.group(2)
            
            # Parse following lines
            trigger = ''
            price = 0
            ask_price = 0
            bid_price = 0
            confluence_count = 0
            confluence_total = 8  # Default for Trader
            indicator_states = {}
            order_placed = False
            blocked_reason = None
            
            # Look at next ~15 lines for all info
            for j in range(1, 20):
                if i + j < len(lines):
                    next_line = lines[i + j].strip()
                    
                    # End of signal box
                    if '╚' in next_line:
                        # Check lines after box for order status
                        for k in range(j + 1, j + 5):
                            if i + k < len(lines):
                                status_line = lines[i + k].strip()
                                if '>>> ORDER PLACED:' in status_line:
                                    order_placed = True
                                    break
                                elif '>>> OUTSIDE TRADING HOURS:' in status_line:
                                    blocked_reason = 'OUTSIDE_HOURS'
                                    break
                                elif 'BLOCKED by cooldown' in status_line:
                                    blocked_reason = 'COOLDOWN'
                                    break
                                elif '╔' in status_line:
                                    # Next signal box started
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
                    
                    # Indicator state line (contains RR= and multiple indicators)
                    if 'RR=' in next_line and 'DT=' in next_line and 'AIQ1=' not in next_line:
                        indicator_states = parse_indicator_state(next_line)
            
            # Use ask for LONG, bid for SHORT as entry price estimate
            price = ask_price if direction == 'LONG' else bid_price
            
            signals.append({
                'source': 'Trader',
                'time_str': time_str,
                'timestamp': datetime.strptime(f"{date_str} {time_str}", "%Y-%m-%d %H:%M:%S"),
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


def parse_trader_closed_trades(filepath, date_str):
    """
    Parse ActiveNikiTrader log for trade results.
    Format: ❌ TRADE CLOSED: P&L $-340.00 | Daily P&L: $-340.00 (1 trades)
            ✅ TRADE CLOSED: P&L $200.00 | Daily P&L: $200.00 (1 trades)
    """
    closed_trades = []
    if not os.path.exists(filepath):
        return closed_trades
    
    with open(filepath, 'r', encoding='utf-8') as f:
        for line in f:
            line = line.strip()
            
            # Look for trade closed line
            closed_match = re.search(r'TRADE CLOSED: P&L \$([+-]?\d+\.?\d*)', line)
            if closed_match:
                pnl = float(closed_match.group(1))
                
                # Extract log timestamp
                time_match = re.match(r'(\d{2}:\d{2}:\d{2})', line)
                time_str = time_match.group(1) if time_match else '00:00:00'
                
                closed_trades.append({
                    'time_str': time_str,
                    'timestamp': datetime.strptime(f"{date_str} {time_str}", "%Y-%m-%d %H:%M:%S"),
                    'pnl_dollars': pnl,
                    'pnl_ticks': pnl / TICK_VALUE
                })
    
    return closed_trades


def find_signal_files(folder_path, date_str):
    """Find all signal log files in the folder."""
    monitor_files = glob.glob(os.path.join(folder_path, 'ActiveNikiMonitor_*.txt'))
    trader_files = glob.glob(os.path.join(folder_path, 'ActiveNikiTrader_*.txt'))
    
    # Also check for signals.txt (legacy)
    legacy_file = os.path.join(folder_path, 'signals.txt')
    if os.path.exists(legacy_file):
        monitor_files.append(legacy_file)
    
    return monitor_files, trader_files


def merge_signals(monitor_signals, trader_signals):
    """
    Merge signals from both sources, preferring Trader signals when timestamps match.
    Returns deduplicated list sorted by time.
    """
    all_signals = []
    trader_times = set()
    
    # Add all trader signals first (they have more info)
    for sig in trader_signals:
        key = (sig['time_str'], sig['direction'])
        trader_times.add(key)
        all_signals.append(sig)
    
    # Add monitor signals that don't duplicate trader signals
    for sig in monitor_signals:
        key = (sig['time_str'], sig['direction'])
        if key not in trader_times:
            all_signals.append(sig)
    
    # Sort by timestamp
    all_signals.sort(key=lambda x: x['timestamp'])
    
    return all_signals


def build_roundtrips(trades):
    """Match entry/exit trades into round-trips."""
    roundtrips = []
    pending_entry = None
    
    for trade in sorted(trades, key=lambda x: x['timestamp']):
        if not trade['is_close']:
            # This is an entry
            if pending_entry:
                # Previous entry was never closed - mark as incomplete
                roundtrips.append({
                    'entry': pending_entry,
                    'exit': None,
                    'direction': 'LONG' if pending_entry['action'] == 'Buy' else 'SHORT',
                    'pnl_ticks': 0,
                    'complete': False
                })
            pending_entry = trade
        else:
            # This is an exit
            if pending_entry:
                # Calculate P&L
                if pending_entry['action'] == 'Buy':
                    # Long trade: exit - entry
                    pnl_ticks = (trade['price'] - pending_entry['price']) / TICK_SIZE
                    direction = 'LONG'
                else:
                    # Short trade: entry - exit
                    pnl_ticks = (pending_entry['price'] - trade['price']) / TICK_SIZE
                    direction = 'SHORT'
                
                roundtrips.append({
                    'entry': pending_entry,
                    'exit': trade,
                    'direction': direction,
                    'pnl_ticks': pnl_ticks,
                    'complete': True
                })
                pending_entry = None
    
    # Handle any remaining pending entry
    if pending_entry:
        roundtrips.append({
            'entry': pending_entry,
            'exit': None,
            'direction': 'LONG' if pending_entry['action'] == 'Buy' else 'SHORT',
            'pnl_ticks': 0,
            'complete': False
        })
    
    return roundtrips


def match_signals_to_trades(roundtrips, signals, date_str):
    """Match each round-trip to nearest signal within window."""
    for rt in roundtrips:
        if not rt['complete']:
            rt['signal'] = None
            rt['alignment'] = 'INCOMPLETE'
            continue
        
        entry_time = rt['entry']['timestamp']
        rt_direction = rt['direction']
        
        best_signal = None
        best_delta = timedelta(seconds=SIGNAL_WINDOW_SECONDS + 1)
        
        for sig in signals:
            sig_time = sig['timestamp']
            
            # Signal must be BEFORE or AT entry time
            delta = entry_time - sig_time
            if timedelta(0) <= delta <= timedelta(seconds=SIGNAL_WINDOW_SECONDS):
                if delta < best_delta:
                    best_delta = delta
                    best_signal = sig
        
        if best_signal:
            if best_signal['direction'] == rt_direction:
                rt['alignment'] = 'ALIGNED'
            else:
                rt['alignment'] = 'COUNTER'
            rt['signal'] = best_signal
        else:
            rt['alignment'] = 'NO_SIGNAL'
            rt['signal'] = None
    
    return roundtrips


def analyze_confluence_effectiveness(roundtrips):
    """Analyze P&L by confluence level."""
    by_confluence = defaultdict(lambda: {'count': 0, 'wins': 0, 'pnl': 0})
    
    for rt in roundtrips:
        if not rt['complete'] or not rt.get('signal'):
            continue
        
        sig = rt['signal']
        conf_key = f"{sig['confluence_count']}/{sig['confluence_total']}"
        
        by_confluence[conf_key]['count'] += 1
        by_confluence[conf_key]['pnl'] += rt['pnl_ticks']
        if rt['pnl_ticks'] > 0:
            by_confluence[conf_key]['wins'] += 1
    
    return dict(by_confluence)


def analyze_trigger_effectiveness(roundtrips):
    """Analyze P&L by trigger type."""
    by_trigger = defaultdict(lambda: {'count': 0, 'wins': 0, 'pnl': 0})
    
    for rt in roundtrips:
        if not rt['complete'] or not rt.get('signal'):
            continue
        
        sig = rt['signal']
        trigger = sig.get('trigger', 'UNKNOWN')
        
        by_trigger[trigger]['count'] += 1
        by_trigger[trigger]['pnl'] += rt['pnl_ticks']
        if rt['pnl_ticks'] > 0:
            by_trigger[trigger]['wins'] += 1
    
    return dict(by_trigger)


def analyze_indicator_correlation(roundtrips):
    """Analyze which indicator states correlate with winning trades."""
    indicator_stats = defaultdict(lambda: {'up_wins': 0, 'up_losses': 0, 'dn_wins': 0, 'dn_losses': 0})
    
    for rt in roundtrips:
        if not rt['complete'] or not rt.get('signal'):
            continue
        
        sig = rt['signal']
        indicators = sig.get('indicators', {})
        is_win = rt['pnl_ticks'] > 0
        is_long = rt['direction'] == 'LONG'
        
        for ind, state in indicators.items():
            if state == 'UP':
                if is_win:
                    indicator_stats[ind]['up_wins'] += 1
                else:
                    indicator_stats[ind]['up_losses'] += 1
            elif state == 'DN':
                if is_win:
                    indicator_stats[ind]['dn_wins'] += 1
                else:
                    indicator_stats[ind]['dn_losses'] += 1
    
    return dict(indicator_stats)


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


def generate_report(roundtrips, signals, date_str, folder_path=None, trader_closed=None):
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
    
    # All signals section
    lines.append(f"ALL SIGNALS FIRED: {len(signals)}")
    lines.append("-" * 40)
    lines.append("Time      Dir   Source   Trigger              Conf   Price")
    for sig in signals:
        conf_str = f"{sig['confluence_count']}/{sig['confluence_total']}"
        order_marker = " ►" if sig.get('order_placed') else ""
        lines.append(f"{sig['time_str']}  {sig['direction']:5} {sig['source']:8} [{sig['trigger']:18}] {conf_str:5} {sig['price']:.2f}{order_marker}")
    lines.append("  (► = order placed by ActiveNikiTrader)")
    lines.append("")
    
    # Alignment section
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
            
            note = ""
            if up_wr > 60 and up_total >= 3:
                note = "← Strong when UP"
            elif dn_wr > 60 and dn_total >= 3:
                note = "← Strong when DN"
            
            lines.append(f"  {ind:8}  {stats['up_wins']}/{stats['up_losses']} ({up_wr:4.0f}%)  {stats['dn_wins']}/{stats['dn_losses']} ({dn_wr:4.0f}%)  {note}")
        lines.append("")
    
    # Best/worst trades
    lines.append("BEST & WORST TRADES")
    lines.append("-" * 19)
    lines.append("")
    lines.append("TOP 5 WINNERS:")
    for rt in top_5:
        if rt['pnl_ticks'] > 0:
            sig = rt.get('signal')
            if sig:
                sig_info = f"[{sig['source']}:{sig['trigger']}] {sig['confluence_count']}/{sig['confluence_total']}"
            else:
                sig_info = "[NO SIGNAL]"
            lines.append(f"  {rt['entry']['time_str']} {rt['direction']:5} {rt['pnl_ticks']:+5.0f}t {sig_info}")
    lines.append("")
    lines.append("BOTTOM 5 LOSERS:")
    for rt in reversed(bottom_5):
        if rt['pnl_ticks'] < 0:
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
            b = time_buckets[bucket]
            lines.append(f"{bucket:12}: {b['trades']:2} trades, {b['wins']}W, {b['pnl']:+5.0f}t")
    lines.append("")
    
    # Key insights
    lines.append("=" * 80)
    lines.append("KEY INSIGHTS")
    lines.append("=" * 80)
    lines.append("")
    
    # Insight 1: Signal hit rate
    signal_rate = len(aligned) / total_trades * 100 if total_trades > 0 else 0
    lines.append("1. SIGNAL ALIGNMENT:")
    lines.append(f"   - {len(aligned)} of {total_trades} trades ({signal_rate:.0f}%) aligned with signals")
    if len(no_signal) > 0:
        lines.append(f"   - {len(no_signal)} trades ({len(no_signal)/total_trades*100:.0f}%) had no nearby signal")
    lines.append("")
    
    # Insight 2: P&L by alignment
    lines.append("2. P&L BY ALIGNMENT:")
    lines.append(f"   - ALIGNED trades: {aligned_pnl:+.0f}t (${aligned_pnl * TICK_VALUE:+.2f})")
    lines.append(f"   - COUNTER trades: {counter_pnl:+.0f}t (${counter_pnl * TICK_VALUE:+.2f})")
    lines.append(f"   - NO SIGNAL trades: {no_signal_pnl:+.0f}t (${no_signal_pnl * TICK_VALUE:+.2f})")
    lines.append("")
    
    # Insight 3: Best confluence level
    if confluence_stats:
        best_conf = max(confluence_stats.items(), key=lambda x: x[1]['pnl']) if confluence_stats else None
        if best_conf:
            lines.append("3. CONFLUENCE INSIGHTS:")
            lines.append(f"   - Best performing: {best_conf[0]} with {best_conf[1]['pnl']:+.0f}t")
            high_conf = [k for k, v in confluence_stats.items() if int(k.split('/')[0]) >= int(k.split('/')[1]) - 1]
            if high_conf:
                high_conf_pnl = sum(confluence_stats[k]['pnl'] for k in high_conf)
                lines.append(f"   - High confluence (N-1 or higher): {high_conf_pnl:+.0f}t")
    lines.append("")
    
    # Insight 4: Recommendations
    lines.append("4. RECOMMENDATIONS:")
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


def main():
    if len(sys.argv) < 2:
        print("Usage: python Analyze-TradingSession.py <folder_path> [--date YYYY-MM-DD]")
        sys.exit(1)
    
    folder_path = sys.argv[1]
    
    # Get date from argument or folder name
    date_str = None
    if '--date' in sys.argv:
        idx = sys.argv.index('--date')
        if idx + 1 < len(sys.argv):
            date_str = sys.argv[idx + 1]
    
    if not date_str:
        folder_name = os.path.basename(folder_path.rstrip('/\\'))
        if re.match(r'\d{4}-\d{2}-\d{2}', folder_name):
            date_str = folder_name
        else:
            print("Error: Could not determine date. Use --date YYYY-MM-DD")
            sys.exit(1)
    
    # Paths
    trades_path = os.path.join(folder_path, 'trades_final.txt')
    
    # Find signal files
    monitor_files, trader_files = find_signal_files(folder_path, date_str)
    
    print(f"Date: {date_str}")
    print(f"Folder: {folder_path}")
    print(f"Monitor files: {len(monitor_files)}")
    print(f"Trader files: {len(trader_files)}")
    
    # Parse trades
    print(f"\nParsing trades from: {trades_path}")
    trades = parse_trades(trades_path)
    print(f"  Found {len(trades)} trade records")
    
    # Parse all signal files
    all_monitor_signals = []
    all_trader_signals = []
    all_trader_closed = []
    
    for f in monitor_files:
        print(f"Parsing Monitor: {os.path.basename(f)}")
        sigs = parse_monitor_signals(f, date_str)
        print(f"  Found {len(sigs)} signals")
        all_monitor_signals.extend(sigs)
    
    for f in trader_files:
        print(f"Parsing Trader: {os.path.basename(f)}")
        sigs = parse_trader_signals(f, date_str)
        closed = parse_trader_closed_trades(f, date_str)
        print(f"  Found {len(sigs)} signals, {len(closed)} closed trades")
        all_trader_signals.extend(sigs)
        all_trader_closed.extend(closed)
    
    # Merge signals
    all_signals = merge_signals(all_monitor_signals, all_trader_signals)
    print(f"\nTotal unique signals: {len(all_signals)}")
    
    # Build round-trips
    roundtrips = build_roundtrips(trades)
    complete_rts = [rt for rt in roundtrips if rt['complete']]
    print(f"Built {len(complete_rts)} complete round-trips")
    
    # Match signals
    roundtrips = match_signals_to_trades(roundtrips, all_signals, date_str)
    
    # Generate report
    report = generate_report(roundtrips, all_signals, date_str, folder_path, all_trader_closed)
    
    # Output file
    dt = datetime.strptime(date_str, "%Y-%m-%d")
    month_abbr = dt.strftime("%b")
    output_filename = f"{month_abbr}{dt.day:02d}_Trading_Analysis.txt"
    output_path = os.path.join(folder_path, output_filename)
    
    with open(output_path, 'w', encoding='utf-8') as f:
        f.write(report)
    
    print(f"\nAnalysis saved to: {output_path}")
    print(f"  File size: {os.path.getsize(output_path)} bytes")


if __name__ == '__main__':
    main()
