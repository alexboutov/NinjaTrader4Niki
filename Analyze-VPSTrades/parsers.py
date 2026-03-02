"""
Parsers for NinjaTrader log files, signal files, and indicator CSVs.
Modified to support Market Replay dates in log prefixes.
"""

import os
import re
import glob
import csv
from datetime import datetime, timedelta

from config import TICK_VALUE, TICK_SIZE, CSV_INDICATOR_COLUMNS

def extract_log_timestamp(line, default_date_str):
    """
    Helper to extract the YYYY-MM-DD HH:MM:SS prefix from the log.
    Returns (datetime_obj, date_str, time_str)
    """
    # Pattern for "2026-02-09 14:30:05 | "
    prefix_match = re.match(r'^(\d{4}-\d{2}-\d{2})\s+(\d{2}:\d{2}:\d{2})\s*\|', line)
    if prefix_match:
        date_str = prefix_match.group(1)
        time_str = prefix_match.group(2)
        ts = datetime.strptime(f"{date_str} {time_str}", "%Y-%m-%d %H:%M:%S")
        return ts, date_str, time_str
    
    # Fallback for lines without the new prefix (standard NT formatting)
    time_match = re.search(r'(\d{2}:\d{2}:\d{2})', line)
    if time_match:
        time_str = time_match.group(1)
        # Use provided date_str for the date portion
        try:
            ts = datetime.strptime(f"{default_date_str} {time_str}", "%Y-%m-%d %H:%M:%S")
            return ts, default_date_str, time_str
        except:
            pass
            
    return None, default_date_str, "00:00:00"

def parse_trades(filepath):
    """Parse trades_final.txt into list of trade dicts."""
    trades = []
    if not os.path.exists(filepath):
        return trades
    
    content = None
    for encoding in ['utf-8', 'utf-16', 'utf-16-le', 'utf-8-sig', 'latin-1']:
        try:
            with open(filepath, 'r', encoding=encoding) as f:
                content = f.read()
            break
        except (UnicodeDecodeError, UnicodeError):
            continue
    
    if content is None:
        return trades
    
    for line in content.splitlines():
        line = line.strip()
        if not line or "New state='Filled'" not in line:
            continue
        
        # Parse timestamp: 2025-12-19 08:07:46
        ts_match = re.search(r'(\d{4}-\d{2}-\d{2}) (\d{2}:\d{2}:\d{2})', line)
        if not ts_match:
            continue
        
        date_str = ts_match.group(1)
        time_str = ts_match.group(2)
        timestamp = datetime.strptime(f"{date_str} {time_str}", "%Y-%m-%d %H:%M:%S")
        
        action_match = re.search(r"Action='([^']+)'", line)
        action = action_match.group(1) if action_match else ""
        price_match = re.search(r"Fill price=(\d+\.?\d*)", line)
        fill_price = float(price_match.group(1)) if price_match else 0
        is_close = "Name='Close'" in line
        
        if action in ['Buy', 'Buy to cover']:
            direction = 'LONG' if not is_close else 'COVER'
        else:
            direction = 'SHORT' if not is_close else 'CLOSE'
        
        trades.append({
            'timestamp': timestamp,
            'market_date': date_str,
            'time_str': time_str,
            'action': action,
            'direction': direction,
            'price': fill_price,
            'is_close': is_close,
            'raw': line
        })
    return trades

def parse_indicator_state(indicator_str):
    states = {}
    for match in re.finditer(r'(\w+)=(\w+|-?\d+)', indicator_str):
        name = match.group(1)
        value = match.group(2)
        if value == 'UP': states[name] = 'UP'
        elif value == 'DN': states[name] = 'DN'
        elif value.lstrip('-').isdigit():
            states[name] = 'UP' if int(value) > 0 else 'DN'
        else: states[name] = value
    return states

def parse_indicator_csv(filepath, date_str):
    """Parse IndicatorValues CSV file into list of BAR dicts."""
    bars = []
    if not os.path.exists(filepath): return bars
    
    with open(filepath, 'r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        for row in reader:
            try:
                bar_time_str = row.get('BarTime', '')
                timestamp = None
                for fmt in ['%m/%d/%Y %I:%M:%S %p', '%Y-%m-%d %H:%M:%S', '%m/%d/%Y %H:%M:%S']:
                    try:
                        timestamp = datetime.strptime(bar_time_str, fmt)
                        break
                    except ValueError: continue
                
                if not timestamp: continue
                
                close = float(row.get('Close', 0))
                indicators = {}
                for csv_col, short_name in CSV_INDICATOR_COLUMNS.items():
                    val = row.get(csv_col, '')
                    if val.upper() == 'TRUE' or val == '1': indicators[short_name] = 'UP'
                    elif val.upper() == 'FALSE' or val == '0': indicators[short_name] = 'DN'
                    elif val.lstrip('-').isdigit():
                        indicators[short_name] = 'UP' if int(val) > 0 else 'DN'
                    else: indicators[short_name] = val
                
                bars.append({
                    'timestamp': timestamp,
                    'time_str': timestamp.strftime('%Y-%m-%d %H:%M:%S'),
                    'close': close,
                    'indicators': indicators,
                    'bull_conf': int(row.get('BullConf', 0)),
                    'bear_conf': int(row.get('BearConf', 0)),
                    'sw_count': int(row.get('SW_Count', 0)) if row.get('SW_Count', '').lstrip('-').isdigit() else 0,
                    'source': row.get('Source', '')
                })
            except: continue
    bars.sort(key=lambda x: x['timestamp'])
    return bars

def parse_trader_signals(filepath, date_str):
    """Parse ActiveNikiTrader log file with support for date prefixes."""
    signals = []
    if not os.path.exists(filepath): return signals
    
    filename = os.path.basename(filepath)
    source = 'Monitor' if 'Monitor' in filename else 'Trader'
    
    with open(filepath, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    i = 0
    while i < len(lines):
        line = lines[i].strip()
        signal_match = re.search(r'\*\*\* (LONG|SHORT) SIGNAL @', line)
        
        if signal_match:
            direction = signal_match.group(1)
            ts, current_market_date, time_str = extract_log_timestamp(line, date_str)
            
            trigger = ''; price = 0; ask_price = 0; bid_price = 0
            confluence_count = 0; confluence_total = 8
            indicator_states = {}; order_placed = False; blocked_reason = None
            
            for j in range(1, 25):
                if i + j < len(lines):
                    next_line = lines[i + j].strip()
                    if '???' in next_line or '????' in next_line:
                        # Check logic for order placement following the box
                        for k in range(j + 1, j + 8):
                            if i + k < len(lines):
                                status_line = lines[i + k].strip()
                                if '>>> ORDER PLACED:' in status_line: order_placed = True; break
                                elif '>>> OUTSIDE TRADING HOURS:' in status_line: blocked_reason = 'OUTSIDE_HOURS'; break
                                elif 'BLOCKED by cooldown' in status_line: blocked_reason = 'COOLDOWN'; break
                        break
                    
                    if 'Trigger:' in next_line: trigger = next_line.split('Trigger:')[1].strip()
                    if 'Ask:' in next_line: ask_price = float(re.search(r'(\d+\.?\d*)', next_line).group(1))
                    if 'Bid:' in next_line: bid_price = float(re.search(r'(\d+\.?\d*)', next_line).group(1))
                    if 'Price:' in next_line: price = float(re.search(r'Price: (\d+\.?\d*)', next_line).group(1))
                    conf_m = re.search(r'Confluence: (\d+)/(\d+)', next_line)
                    if conf_m: confluence_count, confluence_total = int(conf_m.group(1)), int(conf_m.group(2))
                    if 'RR=' in next_line and 'DT=' in next_line: indicator_states = parse_indicator_state(next_line)
            
            if ask_price > 0 or bid_price > 0:
                price = ask_price if direction == 'LONG' else bid_price
            
            signals.append({
                'source': source, 'market_date': current_market_date, 'time_str': time_str,
                'timestamp': ts, 'direction': direction, 'trigger': trigger, 'price': price,
                'confluence_count': confluence_count, 'confluence_total': confluence_total,
                'indicators': indicator_states, 'order_placed': order_placed, 'blocked_reason': blocked_reason
            })
        i += 1
    return signals

def parse_trader_orders_and_closes(filepath, date_str):
    orders = []; closes = []
    if not os.path.exists(filepath): return orders, closes
    
    with open(filepath, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    current_m_date = date_str
    for i, line in enumerate(lines):
        line_stripped = line.strip()
        ts, line_date, line_time = extract_log_timestamp(line_stripped, current_m_date)
        if line_date: current_m_date = line_date

        if '>>> ORDER PLACED:' in line_stripped:
            direction = 'LONG' if 'LONG' in line_stripped else 'SHORT'
            orders.append({
                'timestamp': ts, 'market_date': line_date, 'time_str': line_time,
                'direction': direction, 'price': 0, 'action': 'Buy' if direction == 'LONG' else 'Sell', 'is_close': False
            })
        
        if '>>> ENTRY FILLED:' in line_stripped and orders:
            p_match = re.search(r'@\s*(\d+\.?\d*)', line_stripped)
            if p_match: orders[-1]['fill_price'] = float(p_match.group(1))

        if 'TRADE CLOSED:' in line_stripped:
            m = re.search(r'TRADE CLOSED:\s*(LONG|SHORT)\s*\|\s*Entry=(\d+\.?\d*)\s*Exit=(\d+\.?\d*)\s*\|\s*([+-]?\d+)t\s*\$([+-]?\d+\.?\d*)\s*\|\s*Reason:\s*(\w+)', line_stripped)
            if m:
                closes.append({
                    'timestamp': ts, 'market_date': line_date, 'time_str': line_time,
                    'direction': m.group(1), 'entry_price': float(m.group(2)), 'exit_price': float(m.group(3)),
                    'pnl_ticks': int(m.group(4)), 'pnl_dollars': float(m.group(5)), 'exit_reason': m.group(6), 'is_win': float(m.group(5)) > 0
                })
    return orders, closes

def find_signal_files(folder_path, date_str):
    """Find all signal log files in the folder."""
    monitor_files = glob.glob(os.path.join(folder_path, 'ActiveNikiMonitor_*.txt'))
    trader_files = glob.glob(os.path.join(folder_path, 'ActiveNikiTrader_*.txt'))
    return monitor_files, trader_files

def find_indicator_csv_files(folder_path):
    """Find all IndicatorValues CSV files in the folder."""
    return glob.glob(os.path.join(folder_path, 'IndicatorValues_*.csv'))

def merge_signals(monitor_signals, trader_signals):
    """Merge and deduplicate signals."""
    all_signals = trader_signals[:]
    trader_keys = {(s['time_str'], s['direction']) for s in trader_signals}
    for sig in monitor_signals:
        if (sig['time_str'], sig['direction']) not in trader_keys:
            all_signals.append(sig)
    all_signals.sort(key=lambda x: x['timestamp'])
    return all_signals

def parse_monitor_signals(filepath, date_str):
    # Monitor now uses trader format, so this is a passthrough to parse_trader_signals
    return parse_trader_signals(filepath, date_str)

def parse_trader_closed_trades(filepath, date_str):
    # Legacy support for old closed trade format
    _, closes = parse_trader_orders_and_closes(filepath, date_str)
    return closes
