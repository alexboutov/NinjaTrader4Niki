"""
Configuration constants for ActiveNiki trading analysis.
"""

# === CORE CONFIGURATION ===
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

# === TRAILING STOP CONFIGURATION ===
# Grid search: Test multiple activation/trail combinations to find optimum
TRAILING_STOP_CONFIGS = [
    # Original configs
    {
        'name': 'Trail-40/20',
        'activation_ticks': 40,
        'trail_distance_ticks': 20,
        'description': 'Activate at +40t, trail 20t (tight)'
    },
    {
        'name': 'Trail-60/30',
        'activation_ticks': 60,
        'trail_distance_ticks': 30,
        'description': 'Activate at +60t, trail 30t'
    },
    {
        'name': 'Trail-80/40',
        'activation_ticks': 80,
        'trail_distance_ticks': 40,
        'description': 'Activate at +80t, trail 40t'
    },
    # Extended grid search - higher activations
    {
        'name': 'Trail-90/45',
        'activation_ticks': 90,
        'trail_distance_ticks': 45,
        'description': 'Activate at +90t, trail 45t'
    },
    {
        'name': 'Trail-100/50',
        'activation_ticks': 100,
        'trail_distance_ticks': 50,
        'description': 'Activate at +100t, trail 50t'
    },
    {
        'name': 'Trail-110/55',
        'activation_ticks': 110,
        'trail_distance_ticks': 55,
        'description': 'Activate at +110t, trail 55t (near TP)'
    },
    # Test different ratios at 80t activation
    {
        'name': 'Trail-80/30',
        'activation_ticks': 80,
        'trail_distance_ticks': 30,
        'description': 'Activate at +80t, trail 30t (tighter)'
    },
    {
        'name': 'Trail-80/50',
        'activation_ticks': 80,
        'trail_distance_ticks': 50,
        'description': 'Activate at +80t, trail 50t (looser)'
    },
    # Test different ratios at 100t activation
    {
        'name': 'Trail-100/40',
        'activation_ticks': 100,
        'trail_distance_ticks': 40,
        'description': 'Activate at +100t, trail 40t (tight)'
    },
    {
        'name': 'Trail-100/30',
        'activation_ticks': 100,
        'trail_distance_ticks': 30,
        'description': 'Activate at +100t, trail 30t (very tight)'
    },
    # Test 2:1 ratio at different levels
    {
        'name': 'Trail-50/25',
        'activation_ticks': 50,
        'trail_distance_ticks': 25,
        'description': 'Activate at +50t, trail 25t'
    },
    {
        'name': 'Trail-70/35',
        'activation_ticks': 70,
        'trail_distance_ticks': 35,
        'description': 'Activate at +70t, trail 35t'
    },
    {
        'name': 'Trail-90/40',
        'activation_ticks': 90,
        'trail_distance_ticks': 40,
        'description': 'Activate at +90t, trail 40t'
    },
]
