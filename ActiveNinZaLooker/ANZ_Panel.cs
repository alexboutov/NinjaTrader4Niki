using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class ActiveNinZaLooker : Indicator
    {
        #region Panel Creation

        private void CreatePanel()
        {
            try
            {
                string settingsDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NinjaTrader 8", "settings");
                panelSettingsFile = System.IO.Path.Combine(settingsDir, "ActiveNinZaLooker_Panel.txt");
                panelTransform    = new TranslateTransform(20, 20);

                LoadPanelSettings();

                controlPanel = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment   = VerticalAlignment.Top,
                    Background          = new SolidColorBrush(Color.FromArgb(235, 20, 22, 32)),
                    Width               = panelWidth,
                    Height              = panelHeight,
                    MinWidth            = minPanelWidth,
                    MinHeight           = minPanelHeight,
                    RenderTransform     = panelTransform,
                    Cursor              = Cursors.Arrow
                };

                controlPanel.MouseLeftButtonDown += Panel_MouseDown;
                controlPanel.MouseLeftButtonUp   += Panel_MouseUp;
                controlPanel.MouseMove           += Panel_MouseMove;
                controlPanel.MouseLeave          += Panel_MouseLeave;

                var outerBorder = new Border
                {
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(70, 70, 95)),
                    BorderThickness = new Thickness(2),
                    CornerRadius    = new CornerRadius(5),
                    Padding         = new Thickness(8)
                };

                var stack = new StackPanel();

                // ── Header ────────────────────────────────────────────────────
                stack.Children.Add(new TextBlock
                {
                    Text                = "ActiveNinZaLooker",
                    FontWeight          = FontWeights.Bold,
                    Foreground          = Brushes.CornflowerBlue,
                    FontSize            = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin              = new Thickness(0, 0, 0, 2)
                });
                stack.Children.Add(new TextBlock
                {
                    Text                = "Signal Monitor · No Orders",
                    Foreground          = Brushes.Gray,
                    FontSize            = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin              = new Thickness(0, 0, 0, 6)
                });

                // ── Mode border (WATCHING / SUBMIT) ────────────────────────
                panelModeBorder = new Border
                {
                    BorderThickness = new Thickness(2),
                    CornerRadius    = new CornerRadius(4),
                    Padding         = new Thickness(4),
                    Margin          = new Thickness(0, 0, 0, 6)
                };
                var modeStack = new StackPanel();

                lblMode = new TextBlock
                {
                    Text                = "● WATCHING",
                    FontWeight          = FontWeights.Bold,
                    FontSize            = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground          = Brushes.DodgerBlue
                };
                modeStack.Children.Add(lblMode);

                lblDirectionPrice = new TextBlock
                {
                    Text                = "",
                    FontSize            = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground          = Brushes.White,
                    Margin              = new Thickness(0, 2, 0, 0)
                };
                modeStack.Children.Add(lblDirectionPrice);

                panelModeBorder.Child = modeStack;
                stack.Children.Add(panelModeBorder);

                // ── Window / cooldown ─────────────────────────────────────────
                lblWindow = new TextBlock
                {
                    Text                = "Window: CLOSED",
                    Foreground          = Brushes.Gray,
                    FontSize            = 9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin              = new Thickness(0, 0, 0, 4)
                };
                stack.Children.Add(lblWindow);

                // ── Confluence summary ────────────────────────────────────────
                lblConfluence = new TextBlock
                {
                    Text                = "Bull: - / Bear: - / 6",
                    Foreground          = Brushes.LightGray,
                    FontSize            = 9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin              = new Thickness(0, 0, 0, 4)
                };
                stack.Children.Add(lblConfluence);

                // ── Divider ────────────────────────────────────────────────────
                stack.Children.Add(MakeDivider());

                // ── AIQ1 trigger row ──────────────────────────────────────────
                stack.Children.Add(MakeIndRow("AIQ_1 Trigger", out lblAIQ1, Brushes.Yellow));

                // ── Confluence section header ─────────────────────────────────
                stack.Children.Add(new TextBlock
                {
                    Text                = "── Confluence ──",
                    Foreground          = Brushes.DimGray,
                    FontSize            = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin              = new Thickness(0, 2, 0, 2)
                });

                // ── Confluence rows with live-toggle checkboxes ───────────────
                stack.Children.Add(MakeConfRow("Ruby River",   out lblRR,  out cbRR,  rtUseRR,
                    v => { rtUseRR  = v; RefreshPanel(); }));
                stack.Children.Add(MakeConfRow("Dragon Trend", out lblDT,  out cbDT,  rtUseDT,
                    v => { rtUseDT  = v; RefreshPanel(); }));
                stack.Children.Add(MakeConfRow("VIDYA Pro",    out lblVY,  out cbVY,  rtUseVY,
                    v => { rtUseVY  = v; RefreshPanel(); }));
                stack.Children.Add(MakeConfRow("Easy Trend",   out lblET,  out cbET,  rtUseET,
                    v => { rtUseET  = v; RefreshPanel(); }));
                stack.Children.Add(MakeConfRow("Solar Wave",   out lblSW,  out cbSW,  rtUseSW,
                    v => { rtUseSW  = v; RefreshPanel(); }));
                stack.Children.Add(MakeConfRow("T3 Pro",       out lblT3P, out cbT3P, rtUseT3P,
                    v => { rtUseT3P = v; RefreshPanel(); }));

                // ── Session count ─────────────────────────────────────────────
                stack.Children.Add(MakeDivider());
                lblSignalCount = new TextBlock
                {
                    Text                = "Signals today: 0",
                    Foreground          = Brushes.Gray,
                    FontSize            = 8,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                stack.Children.Add(lblSignalCount);

                // ── Resize grip ───────────────────────────────────────────────
                var grip = new System.Windows.Controls.Canvas
                {
                    Width               = 12,
                    Height              = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin              = new Thickness(0, 4, 0, 0)
                };
                for (int i = 0; i < 3; i++)
                    grip.Children.Add(new System.Windows.Shapes.Line
                    {
                        X1 = 10 - i * 4, Y1 = 10,
                        X2 = 10,         Y2 = 10 - i * 4,
                        Stroke          = new SolidColorBrush(Color.FromRgb(100, 100, 120)),
                        StrokeThickness = 1
                    });
                stack.Children.Add(grip);

                outerBorder.Child = stack;
                controlPanel.Children.Add(outerBorder);

                (ChartControl.Parent as Grid)?.Children.Add(controlPanel);
                panelActive = true;

                ApplyPanelConstraints();
            }
            catch (Exception ex) { Print($"[ActiveNinZaLooker] Panel error: {ex.Message}"); }
        }

        // Standard read-only indicator row (name left, status right)
        private Grid MakeIndRow(string name, out TextBlock statusLbl, Brush nameBrush)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

            var txt = new TextBlock
            {
                Text              = name,
                Foreground        = nameBrush,
                FontSize          = 9,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(txt, 0);
            row.Children.Add(txt);

            statusLbl = new TextBlock
            {
                Text                = "---",
                Foreground          = Brushes.Gray,
                FontSize            = 9,
                FontWeight          = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Center
            };
            Grid.SetColumn(statusLbl, 1);
            row.Children.Add(statusLbl);

            return row;
        }

        // Confluence row: checkbox | name | status
        // The checkbox toggles the runtime rt* bool and calls the provided setter.
        private Grid MakeConfRow(string name, out TextBlock statusLbl,
                                 out System.Windows.Controls.CheckBox cb,
                                 bool initiallyChecked, Action<bool> onToggle)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });   // checkbox
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });   // status

            var checkbox = new System.Windows.Controls.CheckBox
            {
                IsChecked         = initiallyChecked,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 2, 0),
                // Keep checkboxes small and non-intrusive
                Width  = 13,
                Height = 13
            };
            checkbox.Checked   += (s, e) => onToggle(true);
            checkbox.Unchecked += (s, e) => onToggle(false);
            Grid.SetColumn(checkbox, 0);
            row.Children.Add(checkbox);
            cb = checkbox;

            var txt = new TextBlock
            {
                Text              = name,
                Foreground        = Brushes.White,
                FontSize          = 9,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(txt, 1);
            row.Children.Add(txt);

            statusLbl = new TextBlock
            {
                Text                = "---",
                Foreground          = Brushes.Gray,
                FontSize            = 9,
                FontWeight          = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Center
            };
            Grid.SetColumn(statusLbl, 2);
            row.Children.Add(statusLbl);

            return row;
        }

        private Border MakeDivider() => new Border
        {
            BorderBrush     = Brushes.DimGray,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin          = new Thickness(0, 2, 0, 4)
        };

        #endregion

        #region Panel Removal

        private void RemovePanel()
        {
            try
            {
                if (panelActive && controlPanel != null)
                {
                    controlPanel.MouseLeftButtonDown -= Panel_MouseDown;
                    controlPanel.MouseLeftButtonUp   -= Panel_MouseUp;
                    controlPanel.MouseMove           -= Panel_MouseMove;
                    controlPanel.MouseLeave          -= Panel_MouseLeave;
                    (ChartControl?.Parent as Grid)?.Children.Remove(controlPanel);
                    panelActive = false;
                }
            }
            catch { }
        }

        #endregion

        #region Panel Refresh

        private void RefreshPanel()
        {
            if (!panelActive || ChartControl == null) return;
            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var (bull, bear, total) = GetConfluence();

                    // ── Mode block ────────────────────────────────────────────
                    if (currentMode == PanelMode.Submit)
                    {
                        bool isLong = submitDirection == "LONG";
                        lblMode.Text       = isLong ? "▲ SUBMIT LONG" : "▼ SUBMIT SHORT";
                        lblMode.Foreground = isLong ? Brushes.Lime : Brushes.OrangeRed;
                        panelModeBorder.BorderBrush = isLong
                            ? new SolidColorBrush(Color.FromRgb(0, 220, 80))
                            : new SolidColorBrush(Color.FromRgb(220, 60, 0));
                        panelModeBorder.Background = isLong
                            ? new SolidColorBrush(Color.FromArgb(40, 0, 255, 80))
                            : new SolidColorBrush(Color.FromArgb(40, 255, 80, 0));

                        int aligned = isLong ? submitBull : submitBear;
                        lblDirectionPrice.Text      = $"{submitDirection} @ {submitPrice:F2}  [{aligned}/{submitTotal}]  {submitTime:HH:mm:ss}";
                        lblDirectionPrice.Foreground = isLong ? Brushes.Lime : Brushes.OrangeRed;
                        lblDirectionPrice.Visibility = Visibility.Visible;
                    }
                    else // Watching
                    {
                        lblMode.Text       = "● WATCHING";
                        lblMode.Foreground = Brushes.DodgerBlue;
                        panelModeBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(40, 80, 160));
                        panelModeBorder.Background  = new SolidColorBrush(Color.FromArgb(30, 40, 80, 200));

                        bool bullReady = bull >= MinConfluenceToTrade;
                        bool bearReady = bear >= MinConfluenceToTrade;
                        if (bullReady || bearReady)
                        {
                            lblDirectionPrice.Text = bullReady
                                ? $"Bull {bull}/{MinConfluenceToTrade} — await Yellow ■"
                                : $"Bear {bear}/{MinConfluenceToTrade} — await Orange ■";
                            lblDirectionPrice.Foreground = bullReady ? Brushes.Lime : Brushes.Orange;
                        }
                        else
                        {
                            lblDirectionPrice.Text      = $"Need {MinConfluenceToTrade} | Bull {bull} Bear {bear}";
                            lblDirectionPrice.Foreground = Brushes.Gray;
                        }
                        lblDirectionPrice.Visibility = Visibility.Visible;
                    }

                    // ── Window status ─────────────────────────────────────────
                    bool inCD = CooldownBars > 0 && barsSinceLastSignal >= 0 && barsSinceLastSignal < CooldownBars;
                    if (inCD)
                    {
                        lblWindow.Text      = $"🕐 Cooldown {barsSinceLastSignal}/{CooldownBars}";
                        lblWindow.Foreground = Brushes.Yellow;
                    }
                    else if (barsSinceYellowSquare >= 0 && barsSinceYellowSquare <= MaxBarsAfterSquare)
                    {
                        lblWindow.Text      = $"⚡ LONG Window ({barsSinceYellowSquare}/{MaxBarsAfterSquare})";
                        lblWindow.Foreground = Brushes.Lime;
                    }
                    else if (barsSinceOrangeSquare >= 0 && barsSinceOrangeSquare <= MaxBarsAfterSquare)
                    {
                        lblWindow.Text      = $"⚡ SHORT Window ({barsSinceOrangeSquare}/{MaxBarsAfterSquare})";
                        lblWindow.Foreground = Brushes.Orange;
                    }
                    else
                    {
                        lblWindow.Text      = "Window: CLOSED";
                        lblWindow.Foreground = Brushes.Gray;
                    }

                    // ── Confluence summary ────────────────────────────────────
                    lblConfluence.Text = $"Bull: {bull}  Bear: {bear}  / {total}  (need {MinConfluenceToTrade})";

                    // ── AIQ1 ──────────────────────────────────────────────────
                    SetLbl(lblAIQ1, AIQ1_IsUp, enabled: true);

                    // ── Confluence indicators ─────────────────────────────────
                    SetLbl(lblRR,  RR_IsUp,  rtUseRR);
                    SetLbl(lblDT,  DT_IsUp,  rtUseDT);
                    SetLbl(lblVY,  VY_IsUp,  rtUseVY);
                    SetLbl(lblET,  ET_IsUp,  rtUseET);
                    SetLbl(lblSW,  SW_IsUp,  rtUseSW);
                    SetLbl(lblT3P, T3P_IsUp, rtUseT3P);

                    // ── Signal count ──────────────────────────────────────────
                    if (lblSignalCount != null)
                        lblSignalCount.Text = $"Signals today: {signalCount}";
                }
                catch { }
            });
        }

        private void SetLbl(TextBlock lbl, bool isUp, bool enabled)
        {
            if (lbl == null) return;
            if (!enabled) { lbl.Text = "OFF"; lbl.Foreground = Brushes.DimGray; return; }
            lbl.Text      = isUp ? "UP" : "DN";
            lbl.Foreground = isUp ? Brushes.Lime : Brushes.OrangeRed;
        }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ActiveNinZaLooker[] cacheActiveNinZaLooker;
		public ActiveNinZaLooker ActiveNinZaLooker()
		{
			return ActiveNinZaLooker(Input);
		}

		public ActiveNinZaLooker ActiveNinZaLooker(ISeries<double> input)
		{
			if (cacheActiveNinZaLooker != null)
				for (int idx = 0; idx < cacheActiveNinZaLooker.Length; idx++)
					if (cacheActiveNinZaLooker[idx] != null &&  cacheActiveNinZaLooker[idx].EqualsInput(input))
						return cacheActiveNinZaLooker[idx];
			return CacheIndicator<ActiveNinZaLooker>(new ActiveNinZaLooker(), input, ref cacheActiveNinZaLooker);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ActiveNinZaLooker ActiveNinZaLooker()
		{
			return indicator.ActiveNinZaLooker(Input);
		}

		public Indicators.ActiveNinZaLooker ActiveNinZaLooker(ISeries<double> input )
		{
			return indicator.ActiveNinZaLooker(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ActiveNinZaLooker ActiveNinZaLooker()
		{
			return indicator.ActiveNinZaLooker(Input);
		}

		public Indicators.ActiveNinZaLooker ActiveNinZaLooker(ISeries<double> input )
		{
			return indicator.ActiveNinZaLooker(input);
		}
	}
}

#endregion
