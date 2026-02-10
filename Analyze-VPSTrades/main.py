#!/usr/bin/env python3
"""
Analyze-TradingSession - Modular Edition
Analyzes NinjaTrader trading logs and ActiveNiki signal logs.
Supports both ActiveNikiMonitor (6-indicator) and ActiveNikiTrader (8-indicator) formats.
Parses IndicatorValues CSV for BAR-level analysis.
Includes "what if exit on first adverse flip" analysis.
Includes TRAILING STOP simulation analysis.
Generates {Mon}{DD}_Trading_Analysis.txt report.

Usage: python main.py <folder_path> [--date YYYY-MM-DD]
"""

import sys
import os
import re
from datetime import datetime

from config import TRAILING_STOP_CONFIGS
from parsers import (
    parse_trades, find_signal_files, find_indicator_csv_files,
    parse_monitor_signals, parse_trader_signals,
    parse_trader_orders_and_closes, parse_indicator_csv,
    merge_signals
)
from roundtrips import (
    build_roundtrips, build_roundtrips_from_trader_log,
    match_signals_to_trades, enrich_roundtrips_with_bar_data
)
from report import generate_report


def main():
    if len(sys.argv) < 2:
        print("Usage: python main.py <folder_path> [--date YYYY-MM-DD]")
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
        
        # Show trailing stop configs being tested
        print(f"\nSimulating {len(TRAILING_STOP_CONFIGS)} trailing stop configurations:")
        for config in TRAILING_STOP_CONFIGS:
            print(f"  - {config['name']}: {config['description']}")
    
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
