#region Using declarations
using System;
using System.Linq;
using System.Reflection;
using NinjaTrader.NinjaScript;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// ============================================================================
//   One-click diagnostic indicator to list all namespaces/classes
//   from every loaded NinjaTrader add-on or custom DLL.
//   Attach this to any chart → see Output window for results.
// ============================================================================

namespace NinjaTrader.NinjaScript.Indicators
{
    public class ListLoadedAddOns : Indicator
    {
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "ListLoadedAddOns";
                Description = "Prints all loaded add-on namespaces and classes to the Output window.";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
            }
            else if (State == State.DataLoaded)
            {
                Print("=== Loaded Add-On Assemblies and Classes ===");

                // Enumerate all assemblies currently loaded in AppDomain
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string name = asm.GetName().Name;

                    // Skip NinjaTrader core assemblies to focus on add-ons
                    if (name.StartsWith("NinjaTrader") || name.StartsWith("PresentationCore") ||
                        name.StartsWith("WindowsBase") || name.StartsWith("System") ||
                        name.StartsWith("Microsoft"))
                        continue;

                    Print($"\n--- Assembly: {name} ---");

                    try
                    {
                        // List all public types (classes)
                        var types = asm.GetTypes()
                                       .Where(t => t.IsClass)
                                       .OrderBy(t => t.FullName)
                                       .Select(t => t.FullName);

                        foreach (var t in types)
                            Print("  " + t);
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        Print($"  [Could not load types: {ex.Message}]");
                    }
                    catch (Exception ex)
                    {
                        Print($"  [Error scanning assembly: {ex.Message}]");
                    }
                }

                Print("\n=== End of Add-On Listing ===");
            }
        }

        protected override void OnBarUpdate()
        {
            // no runtime logic; purely diagnostic
        }
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ListLoadedAddOns[] cacheListLoadedAddOns;
		public ListLoadedAddOns ListLoadedAddOns()
		{
			return ListLoadedAddOns(Input);
		}

		public ListLoadedAddOns ListLoadedAddOns(ISeries<double> input)
		{
			if (cacheListLoadedAddOns != null)
				for (int idx = 0; idx < cacheListLoadedAddOns.Length; idx++)
					if (cacheListLoadedAddOns[idx] != null &&  cacheListLoadedAddOns[idx].EqualsInput(input))
						return cacheListLoadedAddOns[idx];
			return CacheIndicator<ListLoadedAddOns>(new ListLoadedAddOns(), input, ref cacheListLoadedAddOns);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ListLoadedAddOns ListLoadedAddOns()
		{
			return indicator.ListLoadedAddOns(Input);
		}

		public Indicators.ListLoadedAddOns ListLoadedAddOns(ISeries<double> input )
		{
			return indicator.ListLoadedAddOns(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ListLoadedAddOns ListLoadedAddOns()
		{
			return indicator.ListLoadedAddOns(Input);
		}

		public Indicators.ListLoadedAddOns ListLoadedAddOns(ISeries<double> input )
		{
			return indicator.ListLoadedAddOns(input);
		}
	}
}

#endregion
