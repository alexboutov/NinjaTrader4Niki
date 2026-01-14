#!/usr/bin/env python3
"""
Analyze-TradingSession.py
Analyzes NinjaTrader trading logs and ActiveNiki signal logs.
Supports both ActiveNikiMonitor (6-indicator) and ActiveNikiTrader (8-indicator) formats.
Parses IndicatorValues CSV for BAR-level analysis.
Includes "what if exit on first adverse flip" analysis.
Generates {Mon}{DD}_Trading_Analysis.txt report.

Usage: python Analyze-TradingSession.py <folder_path> [--date YYYY-MM-DD]
"""

import sys
import os
import re
import glob
import csv
from datetime import datetime, timedelta
from collections import defaultdict

# === CONFIGURATION ===
SIGNAL_WINDOW_SECONDS = 120  # Match trades within 2 minutes of signal
TICK_VALUE = 5.00  # NQ tick value in dollars
TICK_SIZE = 0.25   # NQ tick size

# All possible indicators (superset)
ALL_INDICATORS = ['AAA', 'SB', 'DT', 'ET', 'RR', 'SW', 'T3P', 'VY']

# Indicator columns in CSV (maps CSV column to short name)
CSV_INDICATOR_COLUMNS = {
    'AIQ1_IsUp': 'AIQ1',
    'RR_IsUp': 'RR',
    'DT_Signal': 'DT',
    'VY_IsUp': 'VY',
    'ET_IsUp': 'ET',
    'SW_IsUp': 'SW',
    'T3P_IsUp': 'T3P',
    'AAA_IsUp': 'AAA',
    'SB_IsUp': 'SB'
}


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


def parse_indicator_csv(filepath, date_str):
    """
    Parse IndicatorValues CSV file into list of BAR dicts.
    
    CSV Format:
    BarTime,Close,AIQ1_IsUp,RR_IsUp,DT_Signal,VY_IsUp,ET_IsUp,SW_IsUp,SW_Count,T3P_IsUp,AAA_IsUp,SB_IsUp,BullConf,BearConf,Source
    
    Returns list of dicts with timestamp, close, indicator states, confluence counts
    """
    bars = []
    if not os.path.exists(filepath):
        return bars
    
    with open(filepath, 'r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        
        for row in reader:
            try:
                # Parse timestamp - format varies: "1/13/2026 12:00:00 AM" or "2026-01-13 00:00:00"
                bar_time_str = row.get('BarTime', '')
                timestamp = None
                
                # Try different date formats
                for fmt in ['%m/%d/%Y %I:%M:%S %p', '%Y-%m-%d %H:%M:%S', '%m/%d/%Y %H:%M:%S']:
                    try:
                        timestamp = datetime.strptime(bar_time_str, fmt)
                        break
                    except ValueError:
                        continue
                
                if not timestamp:
                    continue
                
                # Parse close price
                close = float(row.get('Close', 0))
                
                # Parse indicator states
                indicators = {}
                for csv_col, short_name in CSV_INDICATOR_COLUMNS.items():
                    value = row.get(csv_col, '')
                    if value.upper() == 'TRUE' or value == '1':
                        indicators[short_name] = 'UP'
                    elif value.upper() == 'FALSE' or value == '0':
                        indicators[short_name] = 'DN'
                    elif value.lstrip('-').isdigit():
                        # Numeric (like DT_Signal): positive = UP
                        indicators[short_name] = 'UP' if int(value) > 0 else 'DN'
                    else:
                        indicators[short_name] = value
                
                # Parse confluence counts
                bull_conf = int(row.get('BullConf', 0))
                bear_conf = int(row.get('BearConf', 0))
                
                # Parse SW_Count separately (numeric count, not boolean)
                sw_count = int(row.get('SW_Count', 0)) if row.get('SW_Count', '').lstrip('-').isdigit() else 0
                
                # Parse source
                source = row.get('Source', '')
                
                bars.append({
                    'timestamp': timestamp,
                    'time_str': timestamp.strftime('%H:%M:%S'),
                    'close': close,
                    'indicators': indicators,
                    'bull_conf': bull_conf,
                    'bear_conf': bear_conf,
                    'sw_count': sw_count,
                    'source': source
                })
                
            except Exception as e:
                # Skip malformed rows
                continue
    
    # Sort by timestamp
    bars.sort(key=lambda x: x['timestamp'])
    
    return bars


def find_bar_at_time(bars, target_time, tolerance_seconds=60):
    """
    Find the BAR closest to target_time within tolerance.
    Returns the bar dict or None.
    """
    if not bars:
        return None
    
    best_bar = None
    best_delta = timedelta(seconds=tolerance_seconds + 1)
    
    for bar in bars:
        delta = abs(bar['timestamp'] - target_time)
        if delta < best_delta:
            best_delta = delta
            best_bar = bar
    
    if best_delta <= timedelta(seconds=tolerance_seconds):
        return best_bar
    return None


def find_bars_in_range(bars, start_time, end_time):
    """
    Find all BARs between start_time and end_time (inclusive).
    Returns list of bar dicts.
    """
    return [b for b in bars if start_time <= b['timestamp'] <= end_time]


def estimate_actual_exit_time(bars, entry_time, entry_price, direction, sl_points=10.0, tp_points=30.0):
    """
    Scan BARs forward from entry to find when price would have hit SL or TP.
    Uses TIME-BASED limits (10 minutes) to handle both tick and minute data.
    
    Returns dict with:
    - exit_time: timestamp when SL/TP was hit
    - exit_price: price at exit
    - exit_type: 'SL' or 'TP' or 'TIMEOUT' or 'NO_BARS'
    - bars_in_trade: number of bars from entry to exit
    
    If no bars found for trade window, returns dict with exit_type='NO_BARS'.
    """
    if not bars or entry_price == 0:
        return {'exit_type': 'NO_DATA', 'exit_time': entry_time, 'exit_price': entry_price, 'bars_in_trade': 0}
    
    # Calculate SL and TP levels
    if direction == 'LONG':
        sl_price = entry_price - sl_points
        tp_price = entry_price + tp_points
    else:  # SHORT
        sl_price = entry_price + sl_points
        tp_price = entry_price - tp_points
    
    # Time-based limit: search up to 10 minutes after entry
    max_time = entry_time + timedelta(minutes=10)
    
    # Find bars in the trade window (entry_time to entry_time + 10 min)
    bars_in_window = [b for b in bars if entry_time <= b['timestamp'] <= max_time]
    
    if not bars_in_window:
        # No bars found for this time window - trade might be outside CSV coverage
        return {'exit_type': 'NO_BARS', 'exit_time': entry_time, 'exit_price': entry_price, 'bars_in_trade': 0}
    
    # Scan forward looking for SL or TP hit
    for i, bar in enumerate(bars_in_window):
        close = bar['close']
        
        if direction == 'LONG':
            # Check if TP hit (price went up)
            if close >= tp_price:
                return {
                    'exit_time': bar['timestamp'],
                    'exit_price': close,
                    'exit_type': 'TP',
                    'bars_in_trade': i + 1
                }
            # Check if SL hit (price went down)
            if close <= sl_price:
                return {
                    'exit_time': bar['timestamp'],
                    'exit_price': close,
                    'exit_type': 'SL',
                    'bars_in_trade': i + 1
                }
        else:  # SHORT
            # Check if TP hit (price went down)
            if close <= tp_price:
                return {
                    'exit_time': bar['timestamp'],
                    'exit_price': close,
                    'exit_type': 'TP',
                    'bars_in_trade': i + 1
                }
            # Check if SL hit (price went up)
            if close >= sl_price:
                return {
                    'exit_time': bar['timestamp'],
                    'exit_price': close,
                    'exit_type': 'SL',
                    'bars_in_trade': i + 1
                }
    
    # No SL/TP hit found in 10-minute window - return last bar as timeout
    last_bar = bars_in_window[-1]
    return {
        'exit_time': last_bar['timestamp'],
        'exit_price': last_bar['close'],
        'exit_type': 'TIMEOUT',
        'bars_in_trade': len(bars_in_window)
    }


def analyze_indicator_flips_during_trade(bars, entry_time, exit_time, direction, entry_price):
    """
    Analyze which indicators flipped against the trade direction during the trade.
    
    For LONG: track indicators that flipped from UP to DN
    For SHORT: track indicators that flipped from DN to UP
    
    Also tracks the price at each flip to enable "what if" analysis.
    
    Returns dict with flip analysis including first adverse flip details
    """
    trade_bars = find_bars_in_range(bars, entry_time, exit_time)
    
    if len(trade_bars) < 2:
        return {
            'flips': [],
            'bars_in_trade': len(trade_bars),
            'adverse_flips': [],
            'first_adverse_flip': None,
            'trades_with_adverse_flip': 0
        }
    
    flips = []
    first_adverse_flip = None
    
    # Compare each bar to the previous
    for i in range(1, len(trade_bars)):
        prev_bar = trade_bars[i - 1]
        curr_bar = trade_bars[i]
        
        for ind in ['RR', 'DT', 'VY', 'ET', 'SW', 'T3P', 'AAA']:
            prev_state = prev_bar['indicators'].get(ind)
            curr_state = curr_bar['indicators'].get(ind)
            
            if prev_state and curr_state and prev_state != curr_state:
                # Determine if this is an adverse flip
                adverse = False
                if direction == 'LONG' and prev_state == 'UP' and curr_state == 'DN':
                    adverse = True
                elif direction == 'SHORT' and prev_state == 'DN' and curr_state == 'UP':
                    adverse = True
                
                flip_record = {
                    'indicator': ind,
                    'time': curr_bar['time_str'],
                    'timestamp': curr_bar['timestamp'],
                    'from': prev_state,
                    'to': curr_state,
                    'adverse': adverse,
                    'price_at_flip': curr_bar['close']
                }
                
                flips.append(flip_record)
                
                # Track first adverse flip
                if adverse and first_adverse_flip is None:
                    # Calculate hypothetical P&L if we exited at this price
                    if direction == 'LONG':
                        hypo_pnl_ticks = (curr_bar['close'] - entry_price) / TICK_SIZE
                    else:  # SHORT
                        hypo_pnl_ticks = (entry_price - curr_bar['close']) / TICK_SIZE
                    
                    first_adverse_flip = {
                        'indicator': ind,
                        'time': curr_bar['time_str'],
                        'timestamp': curr_bar['timestamp'],
                        'price': curr_bar['close'],
                        'hypothetical_pnl_ticks': hypo_pnl_ticks
                    }
    
    adverse_flips = [f for f in flips if f['adverse']]
    
    return {
        'flips': flips,
        'bars_in_trade': len(trade_bars),
        'adverse_flips': adverse_flips,
        'first_adverse_flip': first_adverse_flip,
        'had_adverse_flip': first_adverse_flip is not None
    }


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


def parse_trader_orders_and_closes(filepath, date_str):
    """
    Parse ActiveNikiTrader log for order placements and trade closes.
    
    ORDER PLACED format (right after signal box):
        >>> ORDER PLACED: LONG @ Market | SL=10.00pts (+0t buffer) TP=30.00pts
    
    TRADE CLOSED format:
        ✅ TRADE CLOSED: P&L $600.00 | Daily P&L: $600.00 (1 trades)
        ❌ TRADE CLOSED: P&L $-185.00 | Daily P&L: $415.00 (2 trades)
    
    Returns tuple: (orders, closes)
    """
    orders = []
    closes = []
    
    if not os.path.exists(filepath):
        return orders, closes
    
    with open(filepath, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    # Track current signal context for orders
    current_signal_time = None
    current_signal_direction = None
    current_signal_price = 0
    
    for i, line in enumerate(lines):
        line_stripped = line.strip()
        
        # Track signal context (for associating orders with signals)
        signal_match = re.search(r'\*\*\* (LONG|SHORT) SIGNAL @ (\d{2}:\d{2}:\d{2}) \*\*\*', line_stripped)
        if signal_match:
            current_signal_direction = signal_match.group(1)
            current_signal_time = signal_match.group(2)
            current_signal_price = 0
            
            # Look for price in next few lines
            for j in range(1, 10):
                if i + j < len(lines):
                    next_line = lines[i + j].strip()
                    ask_match = re.search(r'Ask: (\d+\.?\d*)', next_line)
                    bid_match = re.search(r'Bid: (\d+\.?\d*)', next_line)
                    if ask_match and current_signal_direction == 'LONG':
                        current_signal_price = float(ask_match.group(1))
                    if bid_match and current_signal_direction == 'SHORT':
                        current_signal_price = float(bid_match.group(1))
                    if '╚' in next_line:
                        break
        
        # Parse ORDER PLACED
        order_match = re.search(r'>>> ORDER PLACED: (LONG|SHORT) @ Market', line_stripped)
        if order_match:
            direction = order_match.group(1)
            # Use the signal time/price as entry time/price
            if current_signal_time:
                orders.append({
                    'timestamp': datetime.strptime(f"{date_str} {current_signal_time}", "%Y-%m-%d %H:%M:%S"),
                    'time_str': current_signal_time,
                    'direction': direction,
                    'price': current_signal_price,
                    'action': 'Buy' if direction == 'LONG' else 'Sell',
                    'is_close': False
                })
        
        # Parse TRADE CLOSED
        closed_match = re.search(r'TRADE CLOSED: P&L \$([+-]?\d+\.?\d*)', line_stripped)
        if closed_match:
            pnl_dollars = float(closed_match.group(1))
            
            # Extract log timestamp (format: HH:MM:SS | at start of line after stripping pipe)
            # The log format is: "22:11:40 | ✅ TRADE CLOSED..."
            # But stripped line starts with emoji, look at original
            time_match = re.search(r'(\d{2}:\d{2}:\d{2})', line)
            time_str = time_match.group(1) if time_match else current_signal_time or '00:00:00'
            
            closes.append({
                'timestamp': datetime.strptime(f"{date_str} {time_str}", "%Y-%m-%d %H:%M:%S"),
                'time_str': time_str,
                'pnl_dollars': pnl_dollars,
                'pnl_ticks': pnl_dollars / TICK_VALUE,
                'is_win': pnl_dollars > 0
            })
    
    return orders, closes


def parse_trader_closed_trades(filepath, date_str):
    """
    Parse ActiveNikiTrader log for trade results (legacy function for backward compat).
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


def find_indicator_csv_files(folder_path):
    """Find all IndicatorValues CSV files in the folder."""
    return glob.glob(os.path.join(folder_path, 'IndicatorValues_*.csv'))


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


def build_roundtrips_from_trader_log(orders, closes):
    """
    Build round-trips by matching ORDER PLACED entries with TRADE CLOSED exits.
    Uses P&L directly from TRADE CLOSED lines.
    """
    roundtrips = []
    
    # Sort both by timestamp
    orders_sorted = sorted(orders, key=lambda x: x['timestamp'])
    closes_sorted = sorted(closes, key=lambda x: x['timestamp'])
    
    # Match orders with closes sequentially (each order should have one close)
    close_idx = 0
    for order in orders_sorted:
        if close_idx >= len(closes_sorted):
            # No more closes - incomplete trade
            roundtrips.append({
                'entry': order,
                'exit': None,
                'direction': order['direction'],
                'pnl_ticks': 0,
                'pnl_dollars': 0,
                'complete': False
            })
            continue
        
        close = closes_sorted[close_idx]
        close_idx += 1
        
        # Create exit trade dict for compatibility
        exit_trade = {
            'timestamp': close['timestamp'],
            'time_str': close['time_str'],
            'is_close': True,
            'pnl_dollars': close['pnl_dollars'],
            'pnl_ticks': close['pnl_ticks']
        }
        
        roundtrips.append({
            'entry': order,
            'exit': exit_trade,
            'direction': order['direction'],
            'pnl_ticks': close['pnl_ticks'],
            'pnl_dollars': close['pnl_dollars'],
            'complete': True
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


def enrich_roundtrips_with_bar_data(roundtrips, bars):
    """
    Add BAR-level data to each round-trip:
    - Entry BAR state
    - Estimated actual exit time (from BAR price hitting SL/TP)
    - Indicator flips during trade
    - First adverse flip with hypothetical P&L
    """
    for rt in roundtrips:
        if not rt['complete']:
            continue
        
        entry_time = rt['entry']['timestamp']
        entry_price = rt['entry'].get('price', 0)
        
        # Find entry BAR
        entry_bar = find_bar_at_time(bars, entry_time, tolerance_seconds=120)
        rt['entry_bar'] = entry_bar
        
        # Estimate actual exit time by scanning BARs for SL/TP hit
        # This is more accurate than using TRADE CLOSED log timestamp
        estimated_exit = estimate_actual_exit_time(
            bars, entry_time, entry_price, rt['direction'],
            sl_points=10.0, tp_points=30.0
        )
        rt['estimated_exit'] = estimated_exit
        
        # Check if we have bar data for this trade
        exit_type = estimated_exit.get('exit_type', '') if estimated_exit else ''
        if exit_type in ['NO_BARS', 'NO_DATA']:
            # No bar data for this trade - skip flip analysis
            rt['flip_analysis'] = {
                'flips': [],
                'bars_in_trade': 0,
                'adverse_flips': [],
                'first_adverse_flip': None,
                'had_adverse_flip': False,
                'no_bar_data': True
            }
            rt['flip_exit_difference'] = None
            continue
        
        # Determine exit time to use for flip analysis
        exit_time = estimated_exit['exit_time']
        
        # Analyze indicator flips during trade (using estimated exit time)
        flip_analysis = analyze_indicator_flips_during_trade(
            bars, entry_time, exit_time, rt['direction'], entry_price
        )
        flip_analysis['no_bar_data'] = False
        rt['flip_analysis'] = flip_analysis
        
        # Calculate savings/cost if we had exited on first adverse flip
        first_flip = flip_analysis.get('first_adverse_flip')
        if first_flip:
            actual_pnl = rt['pnl_ticks']
            hypo_pnl = first_flip['hypothetical_pnl_ticks']
            rt['flip_exit_difference'] = hypo_pnl - actual_pnl  # Positive = would have been better
        else:
            rt['flip_exit_difference'] = None
    
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


def analyze_adverse_flips(roundtrips):
    """
    Analyze which indicators most frequently flip against trades.
    Now counts TRADES affected, not total flip events.
    Returns dict of indicator -> stats
    """
    # Track which trades had adverse flips from each indicator
    indicator_trade_stats = defaultdict(lambda: {
        'trades_with_flip': 0,
        'losers_with_flip': 0,
        'winners_with_flip': 0
    })
    
    for rt in roundtrips:
        if not rt['complete']:
            continue
        
        flip_analysis = rt.get('flip_analysis', {})
        adverse_flips = flip_analysis.get('adverse_flips', [])
        is_win = rt['pnl_ticks'] > 0
        
        # Track which indicators flipped in THIS trade (deduplicate)
        indicators_that_flipped = set(f['indicator'] for f in adverse_flips)
        
        for ind in indicators_that_flipped:
            indicator_trade_stats[ind]['trades_with_flip'] += 1
            if is_win:
                indicator_trade_stats[ind]['winners_with_flip'] += 1
            else:
                indicator_trade_stats[ind]['losers_with_flip'] += 1
    
    return dict(indicator_trade_stats)


def analyze_early_exit_impact(roundtrips):
    """
    Analyze the impact of hypothetical early exit on first adverse flip.
    
    Returns summary dict with:
    - Total trades with adverse flips
    - How many would have been better/worse with early exit
    - Total ticks saved/lost
    - Trades skipped due to no bar data
    """
    trades_with_flip = []
    trades_without_flip = 0
    trades_no_bar_data = 0
    
    for rt in roundtrips:
        if not rt['complete']:
            continue
        
        flip_analysis = rt.get('flip_analysis', {})
        
        # Check if this trade had bar data
        if flip_analysis.get('no_bar_data', False):
            trades_no_bar_data += 1
            continue
        
        first_flip = flip_analysis.get('first_adverse_flip')
        estimated_exit = rt.get('estimated_exit')
        
        if first_flip:
            actual_pnl = rt['pnl_ticks']
            hypo_pnl = first_flip['hypothetical_pnl_ticks']
            difference = hypo_pnl - actual_pnl  # Positive = early exit better
            
            trades_with_flip.append({
                'entry_time': rt['entry']['time_str'],
                'direction': rt['direction'],
                'actual_pnl': actual_pnl,
                'hypo_pnl': hypo_pnl,
                'difference': difference,
                'flip_indicator': first_flip['indicator'],
                'flip_time': first_flip['time'],
                'flip_price': first_flip['price'],
                'was_winner': actual_pnl > 0,
                'early_exit_better': difference > 0,
                'estimated_exit_type': estimated_exit['exit_type'] if estimated_exit else 'UNKNOWN',
                'bars_in_trade': estimated_exit['bars_in_trade'] if estimated_exit else 0
            })
        else:
            trades_without_flip += 1
    
    if not trades_with_flip and trades_no_bar_data == 0:
        return None
    
    # Calculate summary stats
    total_trades = len(trades_with_flip)
    early_better_count = sum(1 for t in trades_with_flip if t['early_exit_better'])
    early_worse_count = total_trades - early_better_count
    
    total_difference = sum(t['difference'] for t in trades_with_flip)
    
    # Separate analysis for winners and losers
    losers = [t for t in trades_with_flip if not t['was_winner']]
    winners = [t for t in trades_with_flip if t['was_winner']]
    
    loser_savings = sum(t['difference'] for t in losers) if losers else 0
    winner_cost = sum(t['difference'] for t in winners) if winners else 0
    
    return {
        'trades_analyzed': total_trades,
        'trades_without_flip': trades_without_flip,
        'trades_no_bar_data': trades_no_bar_data,
        'early_exit_better_count': early_better_count,
        'early_exit_worse_count': early_worse_count,
        'total_difference_ticks': total_difference,
        'loser_count': len(losers),
        'loser_savings_ticks': loser_savings,
        'winner_count': len(winners),
        'winner_cost_ticks': winner_cost,
        'trade_details': trades_with_flip
    }


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
            
            # Add flip exit info if available
            flip_info = ""
            first_flip = rt.get('flip_analysis', {}).get('first_adverse_flip')
            if first_flip:
                hypo = first_flip['hypothetical_pnl_ticks']
                diff = rt.get('flip_exit_difference', 0)
                if diff and diff > 0:
                    flip_info = f" [flip@{first_flip['time']}: {hypo:+.0f}t, save {diff:+.0f}t]"
                elif diff and diff < 0:
                    flip_info = f" [flip@{first_flip['time']}: {hypo:+.0f}t, cost {abs(diff):.0f}t]"
            
            lines.append(f"  {rt['entry']['time_str']} {rt['direction']:5} {rt['pnl_ticks']:+6.0f}t (${rt['pnl_ticks'] * TICK_VALUE:+7.2f}) {pnl_marker} {sig_info}{flip_info}")
        lines.append("")
    
    # === EARLY EXIT ANALYSIS (NEW SECTION) ===
    if early_exit_analysis:
        lines.append("=" * 80)
        lines.append("EARLY EXIT ANALYSIS: What if we exited on first adverse indicator flip?")
        lines.append("=" * 80)
        lines.append("")
        lines.append("(Exit times estimated by scanning BAR data for SL/TP price levels)")
        lines.append("")
        
        ea = early_exit_analysis
        
        # Show trades without bar data first if any
        if ea.get('trades_no_bar_data', 0) > 0:
            lines.append(f"Trades SKIPPED (no BAR data for time window): {ea['trades_no_bar_data']}")
            lines.append("  (These trades occurred outside CSV data coverage, e.g., overnight)")
            lines.append("")
        
        if ea['trades_analyzed'] == 0:
            lines.append("No trades with adverse flips to analyze.")
            lines.append("(All trades either had no bar data or no indicator flipped against them)")
            lines.append("")
        else:
            lines.append(f"Trades with adverse flips: {ea['trades_analyzed']} of {total_trades}")
            if ea.get('trades_without_flip', 0) > 0:
                lines.append(f"Trades without adverse flips: {ea['trades_without_flip']} (no flip before SL/TP)")
            lines.append(f"  - Early exit would be BETTER:  {ea['early_exit_better_count']} trades")
            lines.append(f"  - Early exit would be WORSE:   {ea['early_exit_worse_count']} trades")
            lines.append("")
            
            lines.append(f"Impact on LOSING trades ({ea['loser_count']} trades that hit SL):")
            lines.append(f"  - Ticks saved by early exit: {ea['loser_savings_ticks']:+.0f}t (${ea['loser_savings_ticks'] * TICK_VALUE:+.2f})")
            lines.append("")
            
            lines.append(f"Impact on WINNING trades ({ea['winner_count']} trades that hit TP):")
            lines.append(f"  - Ticks lost by early exit:  {ea['winner_cost_ticks']:+.0f}t (${ea['winner_cost_ticks'] * TICK_VALUE:+.2f})")
            lines.append("")
            
            net_impact = ea['total_difference_ticks']
            lines.append(f"NET IMPACT of early exit strategy: {net_impact:+.0f}t (${net_impact * TICK_VALUE:+.2f})")
            if net_impact > 0:
                lines.append(f"  → Early exit would have IMPROVED results by {net_impact:.0f}t")
            else:
                lines.append(f"  → Early exit would have REDUCED results by {abs(net_impact):.0f}t")
            lines.append("")
            
            # Detailed breakdown
            lines.append("TRADE-BY-TRADE BREAKDOWN:")
            lines.append("-" * 85)
            lines.append("Entry     Dir   Exit  Bars  Actual   Flip@     Ind    HypoP&L  Diff    Result")
            lines.append("-" * 85)
            
            for t in ea['trade_details']:
                result = "SAVE" if t['early_exit_better'] else "COST"
                outcome = "W" if t['was_winner'] else "L"
                exit_type = t.get('estimated_exit_type', '?')[:2]  # SL, TP, or TI(meout)
                bars = t.get('bars_in_trade', 0)
                lines.append(
                    f"  {t['entry_time']} {t['direction']:5} {exit_type:4} {bars:4}  {t['actual_pnl']:+6.0f}t  "
                    f"{t['flip_time']}  {t['flip_indicator']:5}  {t['hypo_pnl']:+6.0f}t  "
                    f"{t['difference']:+5.0f}t  {result}({outcome})"
                )
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
    
    # Adverse flip analysis section (simplified - now counts trades, not events)
    if adverse_flip_stats:
        lines.append("INDICATOR FLIP ANALYSIS (during trades)")
        lines.append("-" * 40)
        lines.append("How many trades had each indicator flip against them:")
        lines.append("Indicator   Trades   On Losers   On Winners")
        for ind in sorted(adverse_flip_stats.keys()):
            stats = adverse_flip_stats[ind]
            lines.append(f"  {ind:8}  {stats['trades_with_flip']:6}   {stats['losers_with_flip']:9}   {stats['winners_with_flip']:10}")
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
            
            # Add first adverse flip info
            first_flip = rt.get('flip_analysis', {}).get('first_adverse_flip')
            flip_info = ""
            if first_flip:
                flip_info = f" (1st flip: {first_flip['indicator']}@{first_flip['time']})"
            
            lines.append(f"  {rt['entry']['time_str']} {rt['direction']:5} {rt['pnl_ticks']:+5.0f}t {sig_info}{flip_info}")
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
    
    # Insight 4: Early exit analysis
    if early_exit_analysis:
        ea = early_exit_analysis
        lines.append("4. EARLY EXIT STRATEGY:")
        if ea['total_difference_ticks'] > 0:
            lines.append(f"   - Exiting on first adverse flip would SAVE {ea['total_difference_ticks']:.0f}t")
            lines.append(f"   - Reduces losses by {ea['loser_savings_ticks']:.0f}t, costs {abs(ea['winner_cost_ticks']):.0f}t from winners")
        else:
            lines.append(f"   - Exiting on first adverse flip would COST {abs(ea['total_difference_ticks']):.0f}t")
            lines.append(f"   - Current SL/TP performs better than indicator-based exit")
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
        # Handle both YYYY-MM-DD and YYYY-MM-DD_local formats
        match = re.match(r'(\d{4}-\d{2}-\d{2})', folder_name)
        if match:
            date_str = match.group(1)
        else:
            print("Error: Could not determine date. Use --date YYYY-MM-DD")
            sys.exit(1)
    
    # Paths
    trades_path = os.path.join(folder_path, 'trades_final.txt')
    
    # Find signal files
    monitor_files, trader_files = find_signal_files(folder_path, date_str)
    
    # Find indicator CSV files
    csv_files = find_indicator_csv_files(folder_path)
    
    print(f"Date: {date_str}")
    print(f"Folder: {folder_path}")
    print(f"Monitor files: {len(monitor_files)}")
    print(f"Trader files: {len(trader_files)}")
    print(f"CSV files: {len(csv_files)}")
    
    # Parse trades from trades_final.txt (for discretionary trades)
    print(f"\nParsing trades from: {trades_path}")
    trades = parse_trades(trades_path)
    print(f"  Found {len(trades)} trade records from trades_final.txt")
    
    # Parse all signal files
    all_monitor_signals = []
    all_trader_signals = []
    all_trader_orders = []
    all_trader_closes = []
    
    for f in monitor_files:
        print(f"Parsing Monitor: {os.path.basename(f)}")
        sigs = parse_monitor_signals(f, date_str)
        print(f"  Found {len(sigs)} signals")
        all_monitor_signals.extend(sigs)
    
    for f in trader_files:
        print(f"Parsing Trader: {os.path.basename(f)}")
        sigs = parse_trader_signals(f, date_str)
        orders, closes = parse_trader_orders_and_closes(f, date_str)
        print(f"  Found {len(sigs)} signals, {len(orders)} orders, {len(closes)} closed trades")
        all_trader_signals.extend(sigs)
        all_trader_orders.extend(orders)
        all_trader_closes.extend(closes)
    
    # Parse indicator CSV files for BAR data
    all_bars = []
    for f in csv_files:
        print(f"Parsing CSV: {os.path.basename(f)}")
        bars = parse_indicator_csv(f, date_str)
        print(f"  Found {len(bars)} BAR records")
        all_bars.extend(bars)
    
    # Sort bars by timestamp
    all_bars.sort(key=lambda x: x['timestamp'])
    
    # Show time range of CSV data
    if all_bars:
        first_bar_time = all_bars[0]['timestamp']
        last_bar_time = all_bars[-1]['timestamp']
        print(f"  CSV time range: {first_bar_time.strftime('%H:%M:%S')} to {last_bar_time.strftime('%H:%M:%S')}")
    
    # Merge signals
    all_signals = merge_signals(all_monitor_signals, all_trader_signals)
    print(f"\nTotal unique signals: {len(all_signals)}")
    
    # Build round-trips - prefer trader log data if available, fall back to trades_final.txt
    if all_trader_orders and all_trader_closes:
        print(f"\nBuilding round-trips from trader log ({len(all_trader_orders)} orders, {len(all_trader_closes)} closes)")
        roundtrips = build_roundtrips_from_trader_log(all_trader_orders, all_trader_closes)
    else:
        print("\nBuilding round-trips from trades_final.txt")
        roundtrips = build_roundtrips(trades)
    
    complete_rts = [rt for rt in roundtrips if rt['complete']]
    print(f"Built {len(complete_rts)} complete round-trips")
    
    # Match signals
    roundtrips = match_signals_to_trades(roundtrips, all_signals, date_str)
    
    # Enrich with BAR data if available
    if all_bars:
        print(f"\nEnriching round-trips with BAR data ({len(all_bars)} bars)")
        roundtrips = enrich_roundtrips_with_bar_data(roundtrips, all_bars)
        
        # Count trades with/without bar data
        trades_with_bars = sum(1 for rt in roundtrips if rt['complete'] and not rt.get('flip_analysis', {}).get('no_bar_data', False))
        trades_no_bars = sum(1 for rt in roundtrips if rt['complete'] and rt.get('flip_analysis', {}).get('no_bar_data', False))
        print(f"  Trades with BAR coverage: {trades_with_bars}")
        print(f"  Trades without BAR coverage: {trades_no_bars}")
    
    # Generate report
    report = generate_report(roundtrips, all_signals, date_str, folder_path, all_bars)
    
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
