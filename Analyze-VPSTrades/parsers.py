"""
Parsers for NinjaTrader log files, signal files, and indicator CSVs.
Modified to support Market Replay dates in log prefixes and robust Order Detection.
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
    prefix_match = re.match(r'^(\d{4}-\d{2}-\d{2})\s+(\d{2}:\d{2}:\d{2})\s*\|', line)
    if prefix_match:
        date_str = prefix_match.group(1)
        time_str = prefix_match.group(2)
        ts = datetime.strptime(f"{date_str} {time_str}", "%Y-%m-%d %H:%M:%S")
        return ts, date_str, time_str
    
    time_match = re.search(r'(\d{2}:\d{2}:\d{2})', line)
    if time_match:
        time_str = time_match.group(1)
        try:
            ts = datetime.strptime(f"{default_date_str} {time_str}", "%Y-%m-%d %H:%M:%S")
            return ts, default_date_str, time_str
        except:
            pass
            
    return None, default_date_str, "00:00:00"

def parse_trades(filepath):
    trades = []
    if not os.path.exists(filepath): return trades
    content = None
    for encoding in ['utf-8', 'utf-16', 'utf-16-le', 'utf-8-sig', 'latin-1']:
        try:
            with open(filepath, 'r', encoding=encoding) as f:
                content = f.read()
            break
        except: continue
    if content is None: return trades
    
    for line in content.splitlines():
        line = line.strip()
        if not line or "New state='Filled'" not in line: continue
        ts_match = re.search(r'(\d{4}-\d{2}-\d{2}) (\d{2}:\d{2}:\d{2})', line)
        if not ts_match: continue
        
        date_str, time_str = ts_match.group(1), ts_match.group(2)
        timestamp = datetime.strptime(f"{date_str} {time_str}", "%Y-%m-%d %H:%M:%S")
        action = re.search(r"Action='([^']+)'", line).group(1) if "Action='" in line else ""
        price = float(re.search(r"Fill price=(\d+\.?\d*)", line).group(1)) if "Fill price=" in line else 0
        is_close = "Name='Close'" in line
        direction = ('LONG' if not is_close else 'COVER') if action in ['Buy', 'Buy to cover'] else ('SHORT' if not is_close else 'CLOSE')
        
        trades.append({'timestamp': timestamp, 'market_date': date_str, 'time_str': time_str, 'action': action, 'direction': direction, 'price': price, 'is_close': is_close})
    return trades

def parse_indicator_state(indicator_str):
    states = {}
    for match in re.finditer(r'(\w+)=(\w+|-?\d+)', indicator_str):
        name, value = match.group(1), match.group(2)
        if value == 'UP': states[name] = 'UP'
        elif value == 'DN': states[name] = 'DN'
        elif value.lstrip('-').isdigit(): states[name] = 'UP' if int(value) > 0 else 'DN'
        else: states[name] = value
    return states

def parse_indicator_csv(filepath, date_str):
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
                bars.append({
                    'timestamp': timestamp, 'time_str': timestamp.strftime('%Y-%m-%d %H:%M:%S'),
                    'close': float(row.get('Close', 0)),
                    'indicators': {short_name: row.get(csv_col, '') for csv_col, short_name in CSV_INDICATOR_COLUMNS.items()},
                    'bull_conf': int(row.get('BullConf', 0)), 'bear_conf': int(row.get('BearConf', 0)),
                    'sw_count': int(row.get('SW_Count', 0)) if row.get('SW_Count', '').lstrip('-').isdigit() else 0,
                    'source': row.get('Source', '')
                })
            except: continue
    bars.sort(key=lambda x: x['timestamp'])
    return bars

def parse_trader_signals(filepath, date_str):
    signals = []
    if not os.path.exists(filepath): return signals
    filename = os.path.basename(filepath)
    source = 'Monitor' if 'Monitor' in filename else 'Trader'
    with open(filepath, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    i = 0
    while i < len(lines):
        line = lines[i].strip()
        if '***' in line and 'SIGNAL @' in line:
            direction = 'LONG' if 'LONG' in line else 'SHORT'
            ts, cur_m_date, time_str = extract_log_timestamp(line, date_str)
            trigger, price, confluence_count, order_placed = '', 0, 0, False
            indicator_states = {}
            
            for j in range(1, 25):
                if i + j >= len(lines): break
                nxt = lines[i + j].strip()
                if '>>> ORDER PLACED:' in nxt: order_placed = True
                if 'Trigger:' in nxt: trigger = nxt.split('Trigger:')[1].strip()
                if 'Ask:' in nxt or 'Bid:' in nxt or 'Price:' in nxt:
                    m = re.search(r'(\d+\.?\d*)', nxt)
                    if m: price = float(m.group(1))
                m_c = re.search(r'Confluence:\s*(\d+)/(\d+)', nxt)
                if m_c: confluence_count = int(m_c.group(1))
                if 'RR=' in nxt and 'DT=' in nxt: indicator_states = parse_indicator_state(nxt)
                if j > 5 and '***' in nxt and 'SIGNAL @' in nxt: break
            
            signals.append({
                'source': source, 'market_date': cur_m_date, 'time_str': time_str,
                'timestamp': ts, 'direction': direction, 'trigger': trigger, 'price': price,
                'confluence_count': confluence_count, 'confluence_total': 8,
                'indicators': indicator_states, 'order_placed': order_placed
            })
        i += 1
    return signals

def parse_trader_orders_and_closes(filepath, date_str):
    orders, closes = [], []
    if not os.path.exists(filepath): return orders, closes
    with open(filepath, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    cur_m_date = date_str
    for i, line in enumerate(lines):
        ts, line_date, line_time = extract_log_timestamp(line.strip(), cur_m_date)
        if line_date: cur_m_date = line_date
        if '>>> ORDER PLACED:' in line:
            direction = 'LONG' if 'LONG' in line else 'SHORT'
            orders.append({'timestamp': ts, 'market_date': line_date, 'time_str': line_time, 'direction': direction, 'price': 0, 'action': 'Buy' if direction == 'LONG' else 'Sell', 'is_close': False})
        if '>>> ENTRY FILLED:' in line and orders:
            m = re.search(r'@\s*(\d+\.?\d*)', line)
            if m: orders[-1]['fill_price'] = float(m.group(1))
        if 'TRADE CLOSED:' in line:
            m = re.search(r'TRADE CLOSED:\s*(LONG|SHORT)\s*\|\s*Entry=(\d+\.?\d*)\s*Exit=(\d+\.?\d*)\s*\|\s*([+-]?\d+)t\s*\$([+-]?\d+\.?\d*)\s*\|\s*Reason:\s*(\w+)', line)
            if m: closes.append({'timestamp': ts, 'market_date': line_date, 'time_str': line_time, 'direction': m.group(1), 'entry_price': float(m.group(2)), 'exit_price': float(m.group(3)), 'pnl_ticks': int(m.group(4)), 'pnl_dollars': float(m.group(5)), 'exit_reason': m.group(6), 'is_win': float(m.group(5)) > 0})
    return orders, closes

def find_signal_files(folder_path, date_str):
    return glob.glob(os.path.join(folder_path, 'ActiveNikiMonitor_*.txt')), glob.glob(os.path.join(folder_path, 'ActiveNikiTrader_*.txt'))

def find_indicator_csv_files(folder_path):
    return glob.glob(os.path.join(folder_path, 'IndicatorValues_*.csv'))

def merge_signals(monitor_signals, trader_signals):
    all_sigs = trader_signals[:]
    trader_keys = {(s['timestamp'], s['direction']) for s in trader_signals}
    for sig in monitor_signals:
        if (sig['timestamp'], sig['direction']) not in trader_keys: all_sigs.append(sig)
    all_sigs.sort(key=lambda x: x['timestamp'])
    return all_sigs

def parse_monitor_signals(filepath, date_str): return parse_trader_signals(filepath, date_str)
def parse_trader_closed_trades(filepath, date_str): _, cl = parse_trader_orders_and_closes(filepath, date_str); return cl
