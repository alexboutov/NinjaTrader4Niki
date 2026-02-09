"""
BAR data utilities and trade simulation functions.
Includes trailing stop simulation and indicator flip analysis.
"""

from datetime import timedelta
from config import TICK_SIZE, TICK_VALUE


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


def simulate_trailing_stop(bars, entry_time, entry_price, direction, 
                           sl_ticks=40, tp_ticks=120,
                           activation_ticks=60, trail_distance_ticks=30):
    """
    Simulate a trailing stop exit strategy by scanning BAR data.
    
    Parameters:
    - bars: List of BAR data dicts
    - entry_time: Entry timestamp
    - entry_price: Entry price
    - direction: 'LONG' or 'SHORT'
    - sl_ticks: Fixed stop loss in ticks (e.g., 40 = 10 points for NQ)
    - tp_ticks: Fixed take profit in ticks (e.g., 120 = 30 points for NQ)
    - activation_ticks: Profit level (in ticks) to activate trailing stop
    - trail_distance_ticks: Trail distance behind price (in ticks)
    
    Returns dict with:
    - exit_type: 'TP', 'SL', 'TRAIL', 'TIMEOUT', 'NO_BARS'
    - exit_price: Price at exit
    - exit_time: Timestamp of exit
    - exit_pnl_ticks: P&L in ticks at exit
    - trail_activated: Whether trail was activated
    - max_profit_ticks: Maximum profit reached during trade
    - trail_details: List of trail stop movements (for debugging)
    """
    if not bars or entry_price == 0:
        return {
            'exit_type': 'NO_DATA',
            'exit_time': entry_time,
            'exit_price': entry_price,
            'exit_pnl_ticks': 0,
            'trail_activated': False,
            'max_profit_ticks': 0,
            'trail_details': []
        }
    
    # Convert ticks to price points (NQ: 4 ticks = 1 point)
    sl_points = sl_ticks * TICK_SIZE
    tp_points = tp_ticks * TICK_SIZE
    activation_points = activation_ticks * TICK_SIZE
    trail_distance_points = trail_distance_ticks * TICK_SIZE
    
    # Calculate fixed SL and TP levels
    if direction == 'LONG':
        fixed_sl = entry_price - sl_points
        fixed_tp = entry_price + tp_points
    else:  # SHORT
        fixed_sl = entry_price + sl_points
        fixed_tp = entry_price - tp_points
    
    # Time-based limit: search up to 10 minutes after entry
    max_time = entry_time + timedelta(minutes=10)
    
    # Find bars in the trade window
    bars_in_window = [b for b in bars if entry_time <= b['timestamp'] <= max_time]
    
    if not bars_in_window:
        return {
            'exit_type': 'NO_BARS',
            'exit_time': entry_time,
            'exit_price': entry_price,
            'exit_pnl_ticks': 0,
            'trail_activated': False,
            'max_profit_ticks': 0,
            'trail_details': []
        }
    
    # Initialize trailing stop state
    trail_activated = False
    trail_stop = None
    max_profit_points = 0
    trail_details = []
    
    # Scan bars
    for bar in bars_in_window:
        close = bar['close']
        
        # Calculate current P&L
        if direction == 'LONG':
            current_pnl_points = close - entry_price
        else:
            current_pnl_points = entry_price - close
        
        current_pnl_ticks = current_pnl_points / TICK_SIZE
        
        # Track max profit
        if current_pnl_points > max_profit_points:
            max_profit_points = current_pnl_points
        
        # Check fixed TP first (always honored)
        if direction == 'LONG' and close >= fixed_tp:
            return {
                'exit_type': 'TP',
                'exit_time': bar['timestamp'],
                'exit_price': close,
                'exit_pnl_ticks': tp_ticks,
                'trail_activated': trail_activated,
                'max_profit_ticks': max_profit_points / TICK_SIZE,
                'trail_details': trail_details
            }
        elif direction == 'SHORT' and close <= fixed_tp:
            return {
                'exit_type': 'TP',
                'exit_time': bar['timestamp'],
                'exit_price': close,
                'exit_pnl_ticks': tp_ticks,
                'trail_activated': trail_activated,
                'max_profit_ticks': max_profit_points / TICK_SIZE,
                'trail_details': trail_details
            }
        
        # Check if trailing stop should activate
        if not trail_activated and current_pnl_points >= activation_points:
            trail_activated = True
            # Set initial trail stop
            if direction == 'LONG':
                trail_stop = close - trail_distance_points
            else:
                trail_stop = close + trail_distance_points
            trail_details.append({
                'time': bar['time_str'],
                'action': 'ACTIVATED',
                'price': close,
                'trail_stop': trail_stop,
                'pnl_ticks': current_pnl_ticks
            })
        
        # Update trailing stop if activated
        if trail_activated:
            if direction == 'LONG':
                # Move trail up only
                new_trail = close - trail_distance_points
                if new_trail > trail_stop:
                    trail_stop = new_trail
                    trail_details.append({
                        'time': bar['time_str'],
                        'action': 'TRAIL_UP',
                        'price': close,
                        'trail_stop': trail_stop,
                        'pnl_ticks': current_pnl_ticks
                    })
                
                # Check if trail stop hit
                if close <= trail_stop:
                    exit_pnl_ticks = (trail_stop - entry_price) / TICK_SIZE
                    return {
                        'exit_type': 'TRAIL',
                        'exit_time': bar['timestamp'],
                        'exit_price': trail_stop,
                        'exit_pnl_ticks': exit_pnl_ticks,
                        'trail_activated': True,
                        'max_profit_ticks': max_profit_points / TICK_SIZE,
                        'trail_details': trail_details
                    }
            else:  # SHORT
                # Move trail down only
                new_trail = close + trail_distance_points
                if new_trail < trail_stop:
                    trail_stop = new_trail
                    trail_details.append({
                        'time': bar['time_str'],
                        'action': 'TRAIL_DN',
                        'price': close,
                        'trail_stop': trail_stop,
                        'pnl_ticks': current_pnl_ticks
                    })
                
                # Check if trail stop hit
                if close >= trail_stop:
                    exit_pnl_ticks = (entry_price - trail_stop) / TICK_SIZE
                    return {
                        'exit_type': 'TRAIL',
                        'exit_time': bar['timestamp'],
                        'exit_price': trail_stop,
                        'exit_pnl_ticks': exit_pnl_ticks,
                        'trail_activated': True,
                        'max_profit_ticks': max_profit_points / TICK_SIZE,
                        'trail_details': trail_details
                    }
        
        # Check fixed SL (if trail not activated or price went below trail)
        if direction == 'LONG' and close <= fixed_sl:
            return {
                'exit_type': 'SL',
                'exit_time': bar['timestamp'],
                'exit_price': close,
                'exit_pnl_ticks': -sl_ticks,
                'trail_activated': trail_activated,
                'max_profit_ticks': max_profit_points / TICK_SIZE,
                'trail_details': trail_details
            }
        elif direction == 'SHORT' and close >= fixed_sl:
            return {
                'exit_type': 'SL',
                'exit_time': bar['timestamp'],
                'exit_price': close,
                'exit_pnl_ticks': -sl_ticks,
                'trail_activated': trail_activated,
                'max_profit_ticks': max_profit_points / TICK_SIZE,
                'trail_details': trail_details
            }
    
    # Timeout - use last bar price
    last_bar = bars_in_window[-1]
    if direction == 'LONG':
        exit_pnl_ticks = (last_bar['close'] - entry_price) / TICK_SIZE
    else:
        exit_pnl_ticks = (entry_price - last_bar['close']) / TICK_SIZE
    
    return {
        'exit_type': 'TIMEOUT',
        'exit_time': last_bar['timestamp'],
        'exit_price': last_bar['close'],
        'exit_pnl_ticks': exit_pnl_ticks,
        'trail_activated': trail_activated,
        'max_profit_ticks': max_profit_points / TICK_SIZE,
        'trail_details': trail_details
    }


def analyze_indicator_flips_during_trade(bars, entry_time, exit_time, direction, entry_price, min_confluence=6):
    """
    Analyze both:
    1. When confluence drops below threshold during the trade
    2. When any single indicator flips against trade direction
    
    For LONG: 
      - Confluence drop: BullConf drops below min_confluence
      - Indicator flip: any indicator goes UP→DN
    For SHORT: 
      - Confluence drop: BearConf drops below min_confluence
      - Indicator flip: any indicator goes DN→UP
    
    Returns dict with both analyses
    """
    trade_bars = find_bars_in_range(bars, entry_time, exit_time)
    
    if len(trade_bars) < 2:
        return {
            'bars_in_trade': len(trade_bars),
            'confluence_drop': None,
            'had_confluence_drop': False,
            'first_adverse_flip': None,
            'had_adverse_flip': False
        }
    
    first_confluence_drop = None
    first_adverse_flip = None
    
    # Get entry confluence
    entry_bar = trade_bars[0]
    if direction == 'LONG':
        entry_confluence = entry_bar.get('bull_conf', 0)
    else:
        entry_confluence = entry_bar.get('bear_conf', 0)
    
    # Scan bars for both conditions
    for i in range(1, len(trade_bars)):
        prev_bar = trade_bars[i - 1]
        curr_bar = trade_bars[i]
        
        # === Check confluence drop ===
        if direction == 'LONG':
            curr_confluence = curr_bar.get('bull_conf', 0)
        else:
            curr_confluence = curr_bar.get('bear_conf', 0)
        
        if curr_confluence < min_confluence and first_confluence_drop is None:
            if direction == 'LONG':
                hypo_pnl_ticks = (curr_bar['close'] - entry_price) / TICK_SIZE
            else:
                hypo_pnl_ticks = (entry_price - curr_bar['close']) / TICK_SIZE
            
            first_confluence_drop = {
                'time': curr_bar['time_str'],
                'timestamp': curr_bar['timestamp'],
                'price': curr_bar['close'],
                'entry_confluence': entry_confluence,
                'exit_confluence': curr_confluence,
                'hypothetical_pnl_ticks': hypo_pnl_ticks
            }
        
        # === Check single indicator flips ===
        if first_adverse_flip is None:
            for ind in ['RR', 'DT', 'VY', 'ET', 'SW', 'T3P', 'AAA']:
                prev_state = prev_bar['indicators'].get(ind)
                curr_state = curr_bar['indicators'].get(ind)
                
                if prev_state and curr_state and prev_state != curr_state:
                    adverse = False
                    if direction == 'LONG' and prev_state == 'UP' and curr_state == 'DN':
                        adverse = True
                    elif direction == 'SHORT' and prev_state == 'DN' and curr_state == 'UP':
                        adverse = True
                    
                    if adverse:
                        if direction == 'LONG':
                            hypo_pnl_ticks = (curr_bar['close'] - entry_price) / TICK_SIZE
                        else:
                            hypo_pnl_ticks = (entry_price - curr_bar['close']) / TICK_SIZE
                        
                        first_adverse_flip = {
                            'indicator': ind,
                            'time': curr_bar['time_str'],
                            'timestamp': curr_bar['timestamp'],
                            'price': curr_bar['close'],
                            'hypothetical_pnl_ticks': hypo_pnl_ticks
                        }
                        break  # Found first flip, stop checking other indicators
    
    return {
        'bars_in_trade': len(trade_bars),
        'entry_confluence': entry_confluence,
        'confluence_drop': first_confluence_drop,
        'had_confluence_drop': first_confluence_drop is not None,
        'first_adverse_flip': first_adverse_flip,
        'had_adverse_flip': first_adverse_flip is not None
    }
