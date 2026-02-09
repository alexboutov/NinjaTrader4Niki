"""
Round-trip trade matching, signal alignment, and BAR data enrichment.
"""

from datetime import timedelta
from config import TICK_SIZE, SIGNAL_WINDOW_SECONDS, TRAILING_STOP_CONFIGS
from simulation import (
    find_bar_at_time, estimate_actual_exit_time,
    analyze_indicator_flips_during_trade, simulate_trailing_stop
)


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
    NEW FORMAT includes: direction, entry_price, exit_price, pnl_ticks, exit_reason, slippage
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
                'entry_price': order.get('entry_price', 0),
                'exit_price': 0,
                'pnl_ticks': 0,
                'pnl_dollars': 0,
                'exit_reason': 'INCOMPLETE',
                'entry_slippage_ticks': order.get('entry_slippage_ticks', 0),
                'exit_slippage_ticks': 0,
                'complete': False
            })
            continue
        
        close = closes_sorted[close_idx]
        close_idx += 1
        
        # Get entry price - prefer from close data (more accurate fill price), fallback to order
        entry_price = close.get('entry_price', order.get('entry_price', 0))
        exit_price = close.get('exit_price', 0)
        exit_reason = close.get('exit_reason', 'UNKNOWN')
        
        # Get slippage data
        entry_slippage_ticks = order.get('entry_slippage_ticks', 0)
        exit_slippage_ticks = close.get('exit_slippage_ticks', 0)
        
        # Create exit trade dict for compatibility
        exit_trade = {
            'timestamp': close['timestamp'],
            'time_str': close['time_str'],
            'is_close': True,
            'pnl_dollars': close['pnl_dollars'],
            'pnl_ticks': close['pnl_ticks'],
            'exit_price': exit_price,
            'exit_reason': exit_reason,
            'exit_slippage_ticks': exit_slippage_ticks
        }
        
        roundtrips.append({
            'entry': order,
            'exit': exit_trade,
            'direction': close.get('direction', order['direction']),
            'entry_price': entry_price,
            'exit_price': exit_price,
            'pnl_ticks': close['pnl_ticks'],
            'pnl_dollars': close['pnl_dollars'],
            'exit_reason': exit_reason,
            'entry_slippage_ticks': entry_slippage_ticks,
            'exit_slippage_ticks': exit_slippage_ticks,
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
    - Confluence drop analysis
    - Single indicator flip analysis
    - Trailing stop simulations
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
            # No bar data for this trade - skip analysis
            rt['flip_analysis'] = {
                'bars_in_trade': 0,
                'confluence_drop': None,
                'had_confluence_drop': False,
                'first_adverse_flip': None,
                'had_adverse_flip': False,
                'no_bar_data': True
            }
            rt['confluence_exit_difference'] = None
            rt['flip_exit_difference'] = None
            rt['trailing_stop_analysis'] = {}
            continue
        
        # Determine exit time to use for analysis
        exit_time = estimated_exit['exit_time']
        
        # Analyze both exit strategies during trade
        flip_analysis = analyze_indicator_flips_during_trade(
            bars, entry_time, exit_time, rt['direction'], entry_price,
            min_confluence=6  # MinConfluenceForAutoTrade threshold
        )
        flip_analysis['no_bar_data'] = False
        rt['flip_analysis'] = flip_analysis
        
        actual_pnl = rt['pnl_ticks']
        
        # Calculate difference for confluence drop exit
        confluence_drop = flip_analysis.get('confluence_drop')
        if confluence_drop:
            hypo_pnl = confluence_drop['hypothetical_pnl_ticks']
            rt['confluence_exit_difference'] = hypo_pnl - actual_pnl
        else:
            rt['confluence_exit_difference'] = None
        
        # Calculate difference for single indicator flip exit
        first_flip = flip_analysis.get('first_adverse_flip')
        if first_flip:
            hypo_pnl = first_flip['hypothetical_pnl_ticks']
            rt['flip_exit_difference'] = hypo_pnl - actual_pnl
        else:
            rt['flip_exit_difference'] = None
        
        # === TRAILING STOP SIMULATIONS ===
        rt['trailing_stop_analysis'] = {}
        for config in TRAILING_STOP_CONFIGS:
            trail_result = simulate_trailing_stop(
                bars, entry_time, entry_price, rt['direction'],
                sl_ticks=40, tp_ticks=120,
                activation_ticks=config['activation_ticks'],
                trail_distance_ticks=config['trail_distance_ticks']
            )
            
            # Calculate difference vs actual
            trail_pnl = trail_result['exit_pnl_ticks']
            trail_difference = trail_pnl - actual_pnl
            
            rt['trailing_stop_analysis'][config['name']] = {
                'config': config,
                'result': trail_result,
                'trail_pnl': trail_pnl,
                'actual_pnl': actual_pnl,
                'difference': trail_difference,
                'is_better': trail_difference > 0
            }
    
    return roundtrips
