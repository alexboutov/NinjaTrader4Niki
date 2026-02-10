"""
Statistical analysis functions for trading performance.
"""

from collections import defaultdict
from config import TICK_VALUE, TRAILING_STOP_CONFIGS


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
    Analyze how often confluence drops and indicator flips occur during trades.
    Returns summary stats about both behaviors.
    """
    stats = {
        'total_trades_with_bars': 0,
        # Confluence drop stats
        'trades_with_conf_drop': 0,
        'losers_with_conf_drop': 0,
        'winners_with_conf_drop': 0,
        # Indicator flip stats
        'trades_with_flip': 0,
        'losers_with_flip': 0,
        'winners_with_flip': 0
    }
    
    for rt in roundtrips:
        if not rt['complete']:
            continue
        
        flip_analysis = rt.get('flip_analysis', {})
        
        # Skip trades without bar data
        if flip_analysis.get('no_bar_data', False):
            continue
        
        stats['total_trades_with_bars'] += 1
        is_winner = rt['pnl_ticks'] > 0
        
        # Track confluence drops
        if flip_analysis.get('had_confluence_drop', False):
            stats['trades_with_conf_drop'] += 1
            if is_winner:
                stats['winners_with_conf_drop'] += 1
            else:
                stats['losers_with_conf_drop'] += 1
        
        # Track indicator flips
        if flip_analysis.get('had_adverse_flip', False):
            stats['trades_with_flip'] += 1
            if is_winner:
                stats['winners_with_flip'] += 1
            else:
                stats['losers_with_flip'] += 1
    
    return stats


def analyze_early_exit_impact(roundtrips):
    """
    Analyze the impact of both early exit strategies:
    1. Confluence drop below threshold
    2. Single indicator flip
    
    Returns dict with both analyses for side-by-side comparison
    """
    confluence_trades = []
    flip_trades = []
    trades_no_bar_data = 0
    trades_no_confluence_drop = 0
    trades_no_flip = 0
    
    for rt in roundtrips:
        if not rt['complete']:
            continue
        
        flip_analysis = rt.get('flip_analysis', {})
        
        # Check if this trade had bar data
        if flip_analysis.get('no_bar_data', False):
            trades_no_bar_data += 1
            continue
        
        estimated_exit = rt.get('estimated_exit')
        actual_pnl = rt['pnl_ticks']
        was_winner = actual_pnl > 0
        
        # === Confluence drop analysis ===
        confluence_drop = flip_analysis.get('confluence_drop')
        if confluence_drop:
            hypo_pnl = confluence_drop['hypothetical_pnl_ticks']
            difference = hypo_pnl - actual_pnl
            
            confluence_trades.append({
                'entry_time': rt['entry']['time_str'],
                'direction': rt['direction'],
                'actual_pnl': actual_pnl,
                'hypo_pnl': hypo_pnl,
                'difference': difference,
                'entry_confluence': confluence_drop['entry_confluence'],
                'exit_confluence': confluence_drop['exit_confluence'],
                'trigger_time': confluence_drop['time'],
                'trigger_price': confluence_drop['price'],
                'was_winner': was_winner,
                'early_exit_better': difference > 0,
                'estimated_exit_type': estimated_exit['exit_type'] if estimated_exit else 'UNKNOWN',
                'bars_in_trade': estimated_exit['bars_in_trade'] if estimated_exit else 0
            })
        else:
            trades_no_confluence_drop += 1
        
        # === Single indicator flip analysis ===
        first_flip = flip_analysis.get('first_adverse_flip')
        if first_flip:
            hypo_pnl = first_flip['hypothetical_pnl_ticks']
            difference = hypo_pnl - actual_pnl
            
            flip_trades.append({
                'entry_time': rt['entry']['time_str'],
                'direction': rt['direction'],
                'actual_pnl': actual_pnl,
                'hypo_pnl': hypo_pnl,
                'difference': difference,
                'indicator': first_flip['indicator'],
                'trigger_time': first_flip['time'],
                'trigger_price': first_flip['price'],
                'was_winner': was_winner,
                'early_exit_better': difference > 0,
                'estimated_exit_type': estimated_exit['exit_type'] if estimated_exit else 'UNKNOWN',
                'bars_in_trade': estimated_exit['bars_in_trade'] if estimated_exit else 0
            })
        else:
            trades_no_flip += 1
    
    def summarize(trades_list):
        if not trades_list:
            return None
        total = len(trades_list)
        better = sum(1 for t in trades_list if t['early_exit_better'])
        worse = total - better
        total_diff = sum(t['difference'] for t in trades_list)
        losers = [t for t in trades_list if not t['was_winner']]
        winners = [t for t in trades_list if t['was_winner']]
        loser_savings = sum(t['difference'] for t in losers) if losers else 0
        winner_cost = sum(t['difference'] for t in winners) if winners else 0
        return {
            'trades_analyzed': total,
            'early_exit_better_count': better,
            'early_exit_worse_count': worse,
            'total_difference_ticks': total_diff,
            'loser_count': len(losers),
            'loser_savings_ticks': loser_savings,
            'winner_count': len(winners),
            'winner_cost_ticks': winner_cost,
            'trade_details': trades_list
        }
    
    return {
        'trades_no_bar_data': trades_no_bar_data,
        'confluence': summarize(confluence_trades),
        'trades_no_confluence_drop': trades_no_confluence_drop,
        'flip': summarize(flip_trades),
        'trades_no_flip': trades_no_flip
    }


def analyze_trailing_stop_impact(roundtrips):
    """
    Analyze the impact of trailing stop strategies across all trades.
    
    Returns dict with analysis for each trailing stop configuration.
    """
    results = {}
    trades_no_bar_data = 0
    
    for config in TRAILING_STOP_CONFIGS:
        config_name = config['name']
        results[config_name] = {
            'config': config,
            'trades_analyzed': 0,
            'trades_better': 0,
            'trades_worse': 0,
            'trades_same': 0,
            'total_difference_ticks': 0,
            'total_actual_pnl': 0,
            'total_trail_pnl': 0,
            # Breakdown by actual outcome
            'winners_analyzed': 0,
            'winners_better_with_trail': 0,
            'winners_worse_with_trail': 0,
            'winners_difference': 0,
            'losers_analyzed': 0,
            'losers_better_with_trail': 0,
            'losers_worse_with_trail': 0,
            'losers_difference': 0,
            # Exit type breakdown
            'trail_exits_by_type': defaultdict(int),
            # Let winners run analysis
            'trades_exceeded_tp': 0,  # Trades where price went beyond 120t
            'max_profit_sum': 0,
            # Trade details for report
            'trade_details': []
        }
    
    for rt in roundtrips:
        if not rt['complete']:
            continue
        
        trail_analysis = rt.get('trailing_stop_analysis', {})
        if not trail_analysis:
            trades_no_bar_data += 1
            continue
        
        actual_pnl = rt['pnl_ticks']
        was_winner = actual_pnl > 0
        
        for config_name, analysis in trail_analysis.items():
            if config_name not in results:
                continue
            
            result = analysis['result']
            trail_pnl = analysis['trail_pnl']
            difference = analysis['difference']
            
            r = results[config_name]
            r['trades_analyzed'] += 1
            r['total_actual_pnl'] += actual_pnl
            r['total_trail_pnl'] += trail_pnl
            r['total_difference_ticks'] += difference
            
            # Track exit types
            r['trail_exits_by_type'][result['exit_type']] += 1
            
            # Max profit tracking
            r['max_profit_sum'] += result.get('max_profit_ticks', 0)
            if result.get('max_profit_ticks', 0) > 120:
                r['trades_exceeded_tp'] += 1
            
            # Comparison
            if difference > 0:
                r['trades_better'] += 1
            elif difference < 0:
                r['trades_worse'] += 1
            else:
                r['trades_same'] += 1
            
            # Breakdown by winner/loser
            if was_winner:
                r['winners_analyzed'] += 1
                r['winners_difference'] += difference
                if difference > 0:
                    r['winners_better_with_trail'] += 1
                elif difference < 0:
                    r['winners_worse_with_trail'] += 1
            else:
                r['losers_analyzed'] += 1
                r['losers_difference'] += difference
                if difference > 0:
                    r['losers_better_with_trail'] += 1
                elif difference < 0:
                    r['losers_worse_with_trail'] += 1
            
            # Store detail
            r['trade_details'].append({
                'entry_time': rt['entry']['time_str'],
                'direction': rt['direction'],
                'actual_pnl': actual_pnl,
                'trail_pnl': trail_pnl,
                'difference': difference,
                'exit_type': result['exit_type'],
                'trail_activated': result['trail_activated'],
                'max_profit_ticks': result.get('max_profit_ticks', 0),
                'was_winner': was_winner,
                'is_better': difference > 0
            })
    
    # Convert defaultdicts
    for config_name in results:
        results[config_name]['trail_exits_by_type'] = dict(results[config_name]['trail_exits_by_type'])
    
    return {
        'trades_no_bar_data': trades_no_bar_data,
        'configs': results
    }
