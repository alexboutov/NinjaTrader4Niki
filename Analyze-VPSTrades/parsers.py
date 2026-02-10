"""
Parsers for NinjaTrader log files, signal files, and indicator CSVs.
"""

import os
import re
import glob
import csv
from datetime import datetime, timedelta

from config import TICK_VALUE, TICK_SIZE, CSV_INDICATOR_COLUMNS


def parse_trades(filepath):
    """Parse trades_final.txt into list of trade dicts."""
    trades = []
    if not os.path.exists(filepath):
        return trades
    
    # Try different encodings (NinjaTrader sometimes uses UTF-16)
    content = None
    for encoding in ['utf-8', 'utf-16', 'utf-16-le', 'utf-8-sig', 'latin-1']:
        try:
            with open(filepath, 'r', encoding=encoding) as f:
                content = f.read()
            break
        except (UnicodeDecodeError, UnicodeError):
            continue
    
    if content is None:
        print(f"  Warning: Could not decode {filepath}")
        return trades
    
    for line in content.splitlines():
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
    Parse ActiveNikiTrader or ActiveNikiMonitor log file (both use same box format).
    Format (NEW with date): ║  *** LONG SIGNAL @ 2025-12-07 09:32:34 ***
    Format (OLD time only): ║  *** LONG SIGNAL @ 09:32:34 ***
            ║  Trigger: YellowSquare+RR
            ║  Confluence: 4/5
            ║  RR=UP DT=1 VY=UP ET=UP SW=2 T3P=UP AAA=DN SB=DN
    """
    signals = []
    if not os.path.exists(filepath):
        return signals
    
    # Detect source from filename
    filename = os.path.basename(filepath)
    source = 'Monitor' if 'Monitor' in filename else 'Trader'
    
    with open(filepath, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    i = 0
    while i < len(lines):
        line = lines[i].strip()
        
        # Look for signal line (box format) - try NEW format with date first
        signal_match = re.search(r'\*\*\* (LONG|SHORT) SIGNAL @ (\d{4}-\d{2}-\d{2}) (\d{2}:\d{2}:\d{2}) \*\*\*', line)
        if not signal_match:
            # Fall back to OLD format (time only)
            signal_match = re.search(r'\*\*\* (LONG|SHORT) SIGNAL @ (\d{2}:\d{2}:\d{2}) \*\*\*', line)
        if signal_match:
            direction = signal_match.group(1)
            
            # Check which format was matched (NEW has 3 groups, OLD has 2)
            if len(signal_match.groups()) == 3:
                # NEW format with date: groups are (direction, date, time)
                signal_date = signal_match.group(2)
                time_str = signal_match.group(3)
            else:
                # OLD format: groups are (direction, time) - use provided date_str
                signal_date = date_str
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
                    
                    # End of signal box (handle both proper UTF-8 and corrupted encoding)
                    if '╚' in next_line or 'â•š' in next_line:
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
                    
                    # Ask/Bid prices (Trader format)
                    ask_match = re.search(r'Ask: (\d+\.?\d*)', next_line)
                    if ask_match:
                        ask_price = float(ask_match.group(1))
                    bid_match = re.search(r'Bid: (\d+\.?\d*)', next_line)
                    if bid_match:
                        bid_price = float(bid_match.group(1))
                    
                    # Simple Price: line (Monitor format) - may have timestamp prefix
                    price_match = re.search(r'Price: (\d+\.?\d*)', next_line)
                    if price_match and price == 0:  # Only if not already set
                        price = float(price_match.group(1))
                    
                    # Confluence line
                    conf_match = re.search(r'Confluence: (\d+)/(\d+)', next_line)
                    if conf_match:
                        confluence_count = int(conf_match.group(1))
                        confluence_total = int(conf_match.group(2))
                    
                    # Indicator state line (contains RR= and multiple indicators)
                    if 'RR=' in next_line and 'DT=' in next_line and 'AIQ1=' not in next_line:
                        indicator_states = parse_indicator_state(next_line)
            
            # Use ask for LONG, bid for SHORT if available (Trader format)
            # Otherwise keep existing price (Monitor format)
            if ask_price > 0 or bid_price > 0:
                price = ask_price if direction == 'LONG' else bid_price
            
            signals.append({
                'source': source,
                'time_str': time_str,
                'timestamp': datetime.strptime(f"{signal_date} {time_str}", "%Y-%m-%d %H:%M:%S"),
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
    current_signal_date = None
    current_signal_direction = None
    current_signal_price = 0
    
    for i, line in enumerate(lines):
        line_stripped = line.strip()
        
        # Track signal context (for associating orders with signals)
        # Try NEW format with date first
        signal_match = re.search(r'\*\*\* (LONG|SHORT) SIGNAL @ (\d{4}-\d{2}-\d{2}) (\d{2}:\d{2}:\d{2}) \*\*\*', line_stripped)
        if signal_match:
            current_signal_direction = signal_match.group(1)
            current_signal_date = signal_match.group(2)
            current_signal_time = signal_match.group(3)
            current_signal_price = 0
        else:
            # Fall back to OLD format (time only)
            signal_match = re.search(r'\*\*\* (LONG|SHORT) SIGNAL @ (\d{2}:\d{2}:\d{2}) \*\*\*', line_stripped)
            if signal_match:
                current_signal_direction = signal_match.group(1)
                current_signal_date = date_str  # Use provided date
                current_signal_time = signal_match.group(2)
                current_signal_price = 0
        
        if signal_match:
            
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
            # Use the signal date/time/price as entry
            if current_signal_time and current_signal_date:
                orders.append({
                    'timestamp': datetime.strptime(f"{current_signal_date} {current_signal_time}", "%Y-%m-%d %H:%M:%S"),
                    'time_str': current_signal_time,
                    'direction': direction,
                    'price': current_signal_price,
                    'action': 'Buy' if direction == 'LONG' else 'Sell',
                    'is_close': False
                })
        
        # Parse ENTRY FILLED with slippage
        # Format: >>> ENTRY FILLED: LONG @ 25914.50 | Signal=25914.00 | Slippage: +2t ($10.00) | 2025-12-09 10:41:48
        entry_fill_match = re.search(r'>>> ENTRY FILLED:\s*(LONG|SHORT)\s*@\s*(\d+\.?\d*)\s*\|\s*Signal=(\d+\.?\d*)\s*\|\s*Slippage:\s*([+-]?\d+)t\s*\(\$?([+-]?\d+\.?\d*)\)', line_stripped)
        if entry_fill_match:
            direction = entry_fill_match.group(1)
            fill_price = float(entry_fill_match.group(2))
            signal_price = float(entry_fill_match.group(3))
            entry_slippage_ticks = int(entry_fill_match.group(4))
            entry_slippage_dollars = float(entry_fill_match.group(5))
            
            # Extract trade timestamp from the line (format: 2025-12-09 10:41:48)
            trade_time_match = re.search(r'(\d{4}-\d{2}-\d{2})\s+(\d{2}:\d{2}:\d{2})', line_stripped)
            if trade_time_match:
                trade_date = trade_time_match.group(1)
                trade_time = trade_time_match.group(2)
            else:
                trade_date = current_signal_date if current_signal_date else date_str
                trade_time = current_signal_time if current_signal_time else '00:00:00'
            
            # Update the last order with actual fill info
            if orders and orders[-1]['direction'] == direction:
                orders[-1]['fill_price'] = fill_price
                orders[-1]['signal_price'] = signal_price
                orders[-1]['entry_slippage_ticks'] = entry_slippage_ticks
                orders[-1]['entry_slippage_dollars'] = entry_slippage_dollars
        
        # Parse TRADE CLOSED - NEW FORMAT with direction, entry, exit, reason, and optional exit slippage
        # Format: ✅ TRADE CLOSED: SHORT | Entry=25187.00 Exit=25165.00 | +88t $434.84 | Reason: TRAIL | Exit Slip: +4t
        closed_match = re.search(r'TRADE CLOSED:\s*(LONG|SHORT)\s*\|\s*Entry=(\d+\.?\d*)\s*Exit=(\d+\.?\d*)\s*\|\s*([+-]?\d+)t\s*\$([+-]?\d+\.?\d*)\s*\|\s*Reason:\s*(\w+)(?:\s*\|\s*Exit Slip:\s*([+-]?\d+)t)?', line_stripped)
        if closed_match:
            direction = closed_match.group(1)
            entry_price = float(closed_match.group(2))
            exit_price = float(closed_match.group(3))
            pnl_ticks = int(closed_match.group(4))
            pnl_dollars = float(closed_match.group(5))
            exit_reason = closed_match.group(6)
            exit_slippage_ticks = int(closed_match.group(7)) if closed_match.group(7) else 0
            
            # Extract log timestamp
            time_match = re.search(r'(\d{2}:\d{2}:\d{2})', line)
            time_str = time_match.group(1) if time_match else current_signal_time or '00:00:00'
            
            # Use signal date
            close_date = current_signal_date if current_signal_date else date_str
            
            closes.append({
                'timestamp': datetime.strptime(f"{close_date} {time_str}", "%Y-%m-%d %H:%M:%S"),
                'time_str': time_str,
                'direction': direction,
                'entry_price': entry_price,
                'exit_price': exit_price,
                'pnl_ticks': pnl_ticks,
                'pnl_dollars': pnl_dollars,
                'exit_reason': exit_reason,
                'exit_slippage_ticks': exit_slippage_ticks,
                'is_win': pnl_dollars > 0
            })
        else:
            # Fallback: OLD FORMAT - TRADE CLOSED: P&L $X.XX
            closed_match_old = re.search(r'TRADE CLOSED: P&L \$([+-]?\d+\.?\d*)', line_stripped)
            if closed_match_old:
                pnl_dollars = float(closed_match_old.group(1))
                
                time_match = re.search(r'(\d{2}:\d{2}:\d{2})', line)
                time_str = time_match.group(1) if time_match else current_signal_time or '00:00:00'
                close_date = current_signal_date if current_signal_date else date_str
                
                closes.append({
                    'timestamp': datetime.strptime(f"{close_date} {time_str}", "%Y-%m-%d %H:%M:%S"),
                    'time_str': time_str,
                    'pnl_dollars': pnl_dollars,
                    'pnl_ticks': pnl_dollars / TICK_VALUE,
                    'is_win': pnl_dollars > 0
                })
    
    return orders, closes


def parse_trader_closed_trades(filepath, date_str):
    """
    Parse ActiveNikiTrader log for trade results.
    NEW Format: ✅ TRADE CLOSED: SHORT | Entry=25187.00 Exit=25165.00 | +88t $434.84 | Reason: TRAIL
    OLD Format: ❌ TRADE CLOSED: P&L $-340.00 | Daily P&L: $-340.00 (1 trades)
    """
    closed_trades = []
    if not os.path.exists(filepath):
        return closed_trades
    
    with open(filepath, 'r', encoding='utf-8') as f:
        for line in f:
            line = line.strip()
            
            # Try NEW FORMAT first
            closed_match = re.search(r'TRADE CLOSED:\s*(LONG|SHORT)\s*\|\s*Entry=(\d+\.?\d*)\s*Exit=(\d+\.?\d*)\s*\|\s*([+-]?\d+)t\s*\$([+-]?\d+\.?\d*)\s*\|\s*Reason:\s*(\w+)', line)
            if closed_match:
                direction = closed_match.group(1)
                entry_price = float(closed_match.group(2))
                exit_price = float(closed_match.group(3))
                pnl_ticks = int(closed_match.group(4))
                pnl_dollars = float(closed_match.group(5))
                exit_reason = closed_match.group(6)
                
                time_match = re.match(r'(\d{2}:\d{2}:\d{2})', line)
                time_str = time_match.group(1) if time_match else '00:00:00'
                
                closed_trades.append({
                    'time_str': time_str,
                    'timestamp': datetime.strptime(f"{date_str} {time_str}", "%Y-%m-%d %H:%M:%S"),
                    'direction': direction,
                    'entry_price': entry_price,
                    'exit_price': exit_price,
                    'pnl_ticks': pnl_ticks,
                    'pnl_dollars': pnl_dollars,
                    'exit_reason': exit_reason
                })
            else:
                # Try OLD FORMAT
                closed_match_old = re.search(r'TRADE CLOSED: P&L \$([+-]?\d+\.?\d*)', line)
                if closed_match_old:
                    pnl = float(closed_match_old.group(1))
                    
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
    # ActiveNikiMonitor now uses the same box format as ActiveNikiTrader,
    # so route all files through the trader parser
    monitor_files = []  # No longer used - Monitor uses Trader format
    trader_files = glob.glob(os.path.join(folder_path, 'ActiveNikiMonitor_*.txt'))
    trader_files.extend(glob.glob(os.path.join(folder_path, 'ActiveNikiTrader_*.txt')))
    
    # Note: signals.txt is just a summary file without detail lines (price, confluence, trigger)
    # so we don't parse it - the full data is in the ActiveNikiMonitor/Trader files
    
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
