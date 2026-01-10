#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class ActiveNikiTrader
    {
        #region Chart Panel
        private void CreateControlPanel()
        {
            try
            {
                string settingsDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8", "settings");
                panelSettingsFile = System.IO.Path.Combine(settingsDir, "ActiveNikiTrader_PanelSettings.txt");
                panelTransform = new TranslateTransform(0, 0);
                panelScale = new ScaleTransform(1, 1);
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(panelScale);
                transformGroup.Children.Add(panelTransform);
                LoadPanelSettings();

                controlPanel = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(10, 0, 0, 30),
                    Background = new SolidColorBrush(Color.FromArgb(115, 30, 30, 40)),
                    MinWidth = 200,
                    RenderTransform = transformGroup,
                    RenderTransformOrigin = new Point(0, 1),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                controlPanel.MouseLeftButtonDown += Panel_MouseLeftButtonDown;
                controlPanel.MouseLeftButtonUp += Panel_MouseLeftButtonUp;
                controlPanel.MouseMove += Panel_MouseMove;

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 100)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8)
                };
                
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = "ActiveNiki Trader", FontWeight = FontWeights.Bold, Foreground = Brushes.Cyan, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) });
                lblSubtitle = new TextBlock { Foreground = Brushes.LightGray, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,6) };
                stack.Children.Add(lblSubtitle);
                
                stack.Children.Add(new TextBlock { Text = "── Confluence (8) ──", Foreground = Brushes.Gray, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,2,0,2) });
                stack.Children.Add(CreateRow("Ruby River", ref chkRubyRiver, ref lblRubyRiver, UseRubyRiver));
                stack.Children.Add(CreateRow("Dragon Trend", ref chkDragonTrend, ref lblDragonTrend, UseDragonTrend));
                stack.Children.Add(CreateRow("VIDYA Pro", ref chkVIDYA, ref lblVIDYA, UseVIDYAPro));
                stack.Children.Add(CreateRow("Easy Trend", ref chkEasyTrend, ref lblEasyTrend, UseEasyTrend));
                stack.Children.Add(CreateRow("Solar Wave", ref chkSolarWave, ref lblSolarWave, UseSolarWave));
                stack.Children.Add(CreateRow("T3 Pro", ref chkT3Pro, ref lblT3Pro, UseT3Pro));
                stack.Children.Add(CreateRow("AAA TrendSync", ref chkAAASync, ref lblAAASync, UseAAATrendSync));
                stack.Children.Add(CreateRow("AIQ SuperBands", ref chkSuperBands, ref lblSuperBands, UseAIQSuperBands));
                
                stack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(0,6,0,6) });
                stack.Children.Add(new TextBlock { Text = "── Trigger ──", Foreground = Brushes.Orange, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,2,0,2) });
                
                var aiqRow = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                aiqRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                aiqRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                var lblAIQ1Name = new TextBlock { Text = "AIQ_1 (Yellow ■)", Foreground = Brushes.Orange, FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(lblAIQ1Name, 0); aiqRow.Children.Add(lblAIQ1Name);
                lblAIQ1Status = new TextBlock { Text = "---", Foreground = Brushes.Gray, FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(lblAIQ1Status, 1); aiqRow.Children.Add(lblAIQ1Status);
                stack.Children.Add(aiqRow);
                
                lblWindowStatus = new TextBlock { Text = "Window: CLOSED", Foreground = Brushes.Gray, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,2,0,2) };
                stack.Children.Add(lblWindowStatus);
                
                stack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(0,6,0,6) });

                lblTriggerMode = new TextBlock { Text = $"Signal≥{MinConfluenceRequired} Trade≥{MinConfluenceForAutoTrade} CD={CooldownBars}", Foreground = Brushes.LightGray, FontSize = 9 };
                lblTradeStatus = new TextBlock { Text = EnableAutoTrading ? "⚡ AUTO TRADING ON" : "Mode: Signal Only", Foreground = EnableAutoTrading ? Brushes.Lime : Brushes.Cyan, FontWeight = FontWeights.Bold, FontSize = 10, Margin = new Thickness(0,2,0,2) };
                lblSessionStats = new TextBlock { Text = "Signals: 0", Foreground = Brushes.LightGray, FontSize = 9 };

                stack.Children.Add(lblTriggerMode);
                stack.Children.Add(lblTradeStatus);
                stack.Children.Add(lblSessionStats);
                
                stack.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(0,6,0,6) });
                
                signalBorder = new Border { BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(3), Padding = new Thickness(4) };
                lblLastSignal = new TextBlock { Text = "Waiting for Yellow ■...", Foreground = Brushes.Gray, FontSize = 9, TextWrapping = TextWrapping.Wrap };
                signalBorder.Child = lblLastSignal;
                stack.Children.Add(signalBorder);
                
                border.Child = stack;
                controlPanel.Children.Add(border);

                resizeGrip = new Border { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.SizeNWSE, Margin = new Thickness(0, 0, 2, 2) };
                var gripCanvas = new Canvas { Width = 12, Height = 12 };
                for (int i = 0; i < 3; i++)
                {
                    var line = new Line { X1 = 10 - i * 4, Y1 = 10, X2 = 10, Y2 = 10 - i * 4, Stroke = new SolidColorBrush(Color.FromRgb(120, 120, 140)), StrokeThickness = 1 };
                    gripCanvas.Children.Add(line);
                }
                resizeGrip.Child = gripCanvas;
                resizeGrip.MouseLeftButtonDown += ResizeGrip_MouseLeftButtonDown;
                resizeGrip.MouseLeftButtonUp += ResizeGrip_MouseLeftButtonUp;
                resizeGrip.MouseMove += ResizeGrip_MouseMove;
                controlPanel.Children.Add(resizeGrip);

                UIElementCollection panelHolder = (ChartControl.Parent as Grid)?.Children;
                if (panelHolder != null) panelHolder.Add(controlPanel);
                panelActive = true;
            }
            catch (Exception ex) { Print($"Panel error: {ex.Message}"); }
        }
        
        private Grid CreateRow(string name, ref CheckBox chk, ref TextBlock lbl, bool isChecked)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });
            
            chk = new CheckBox { IsChecked = isChecked, VerticalAlignment = VerticalAlignment.Center };
            chk.Checked += OnChk; chk.Unchecked += OnChk;
            Grid.SetColumn(chk, 0); row.Children.Add(chk);
            
            var txt = new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3,0,0,0) };
            Grid.SetColumn(txt, 1); row.Children.Add(txt);
            
            lbl = new TextBlock { Text = "---", Foreground = Brushes.Gray, FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(lbl, 2); row.Children.Add(lbl);
            return row;
        }
        
        private void OnChk(object s, RoutedEventArgs e)
        {
            UseRubyRiver = chkRubyRiver?.IsChecked ?? false;
            UseDragonTrend = chkDragonTrend?.IsChecked ?? false;
            UseSolarWave = chkSolarWave?.IsChecked ?? false;
            UseVIDYAPro = chkVIDYA?.IsChecked ?? false;
            UseEasyTrend = chkEasyTrend?.IsChecked ?? false;
            UseT3Pro = chkT3Pro?.IsChecked ?? false;
            UseAAATrendSync = chkAAASync?.IsChecked ?? false;
            UseAIQSuperBands = chkSuperBands?.IsChecked ?? false;
        }
        
        private void RemoveControlPanel()
        {
            try
            {
                if (controlPanel != null && panelActive)
                {
                    controlPanel.MouseLeftButtonDown -= Panel_MouseLeftButtonDown;
                    controlPanel.MouseLeftButtonUp -= Panel_MouseLeftButtonUp;
                    controlPanel.MouseMove -= Panel_MouseMove;
                    if (resizeGrip != null)
                    {
                        resizeGrip.MouseLeftButtonDown -= ResizeGrip_MouseLeftButtonDown;
                        resizeGrip.MouseLeftButtonUp -= ResizeGrip_MouseLeftButtonUp;
                        resizeGrip.MouseMove -= ResizeGrip_MouseMove;
                    }
                    UIElementCollection panelHolder = (ChartControl?.Parent as Grid)?.Children;
                    if (panelHolder != null && panelHolder.Contains(controlPanel))
                        panelHolder.Remove(controlPanel);
                    panelActive = false;
                }
            }
            catch { }
        }

        private void Panel_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            isDragging = true;
            dragStartPoint = e.GetPosition(ChartControl?.Parent as UIElement);
            dragStartPoint.X -= panelTransform.X;
            dragStartPoint.Y -= panelTransform.Y;
            controlPanel.CaptureMouse();
            e.Handled = true;
        }

        private void Panel_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (isDragging) { isDragging = false; controlPanel.ReleaseMouseCapture(); SavePanelSettings(); e.Handled = true; }
        }

        private void Panel_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (isDragging)
            {
                Point currentPoint = e.GetPosition(ChartControl?.Parent as UIElement);
                double newX = currentPoint.X - dragStartPoint.X;
                double newY = currentPoint.Y - dragStartPoint.Y;
                var parent = ChartControl?.Parent as FrameworkElement;
                if (parent != null && controlPanel != null)
                {
                    double panelWidth = controlPanel.ActualWidth > 0 ? controlPanel.ActualWidth : 200;
                    double panelHeight = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : 300;
                    double minX = -10, maxX = parent.ActualWidth - panelWidth - 10;
                    double minY = -(parent.ActualHeight - panelHeight - 30), maxY = 0;
                    newX = Math.Max(minX, Math.Min(maxX, newX));
                    newY = Math.Max(minY, Math.Min(maxY, newY));
                }
                panelTransform.X = newX;
                panelTransform.Y = newY;
                e.Handled = true;
            }
        }

        private void ResizeGrip_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            isResizing = true;
            resizeStartPoint = e.GetPosition(ChartControl?.Parent as UIElement);
            resizeStartWidth = panelScale.ScaleX;
            resizeStartHeight = panelScale.ScaleY;
            resizeGrip.CaptureMouse();
            e.Handled = true;
        }

        private void ResizeGrip_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (isResizing) { isResizing = false; resizeGrip.ReleaseMouseCapture(); SavePanelSettings(); e.Handled = true; }
        }

        private void ResizeGrip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (isResizing)
            {
                Point currentPoint = e.GetPosition(ChartControl?.Parent as UIElement);
                double deltaX = currentPoint.X - resizeStartPoint.X;
                double deltaY = currentPoint.Y - resizeStartPoint.Y;
                double baseWidth = controlPanel.ActualWidth > 0 ? controlPanel.ActualWidth / panelScale.ScaleX : 200;
                double avgDelta = (deltaX - deltaY) / 2;
                double newScale = resizeStartWidth + avgDelta / baseWidth;
                newScale = Math.Max(0.5, Math.Min(2.0, newScale));
                panelScale.ScaleX = newScale;
                panelScale.ScaleY = newScale;
                e.Handled = true;
            }
        }

        private void SavePanelSettings()
        {
            try
            {
                if (string.IsNullOrEmpty(panelSettingsFile)) return;
                string dir = System.IO.Path.GetDirectoryName(panelSettingsFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(panelSettingsFile, $"{panelTransform.X},{panelTransform.Y},{panelScale.ScaleX},{panelScale.ScaleY}");
            }
            catch { }
        }

        private void LoadPanelSettings()
        {
            try
            {
                if (string.IsNullOrEmpty(panelSettingsFile) || !File.Exists(panelSettingsFile)) return;
                string content = File.ReadAllText(panelSettingsFile);
                string[] parts = content.Split(',');
                if (parts.Length >= 2 && double.TryParse(parts[0], out double x) && double.TryParse(parts[1], out double y))
                { panelTransform.X = x; panelTransform.Y = y; }
                if (parts.Length >= 4 && double.TryParse(parts[2], out double scaleX) && double.TryParse(parts[3], out double scaleY))
                { panelScale.ScaleX = Math.Max(0.5, Math.Min(2.0, scaleX)); panelScale.ScaleY = Math.Max(0.5, Math.Min(2.0, scaleY)); }
            }
            catch { }
        }
        
        private void UpdatePanel()
        {
            if (!panelActive || ChartControl == null) return;
            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                int enabled = GetEnabledCount();
                if (lblSubtitle != null)
                    lblSubtitle.Text = enabled == 0 ? "No indicators" : $"Min {MinConfluenceRequired}/{enabled} for signal";

                UpdLbl(lblRubyRiver, RR_IsUp, UseRubyRiver);
                UpdLbl(lblDragonTrend, DT_IsUp, UseDragonTrend);
                UpdLbl(lblSolarWave, SW_IsUp, UseSolarWave);
                UpdLbl(lblVIDYA, VY_IsUp, UseVIDYAPro);
                UpdLbl(lblEasyTrend, ET_IsUp, UseEasyTrend);
                UpdLbl(lblT3Pro, T3P_IsUp, UseT3Pro);
                // AAA TrendSync - show N/A if not available, otherwise show UP/DN
                if (lblAAASync != null)
                {
                    if (!UseAAATrendSync)
                    {
                        lblAAASync.Text = "OFF";
                        lblAAASync.Foreground = Brushes.Gray;
                    }
                    else if (!AAA_Available)
                    {
                        lblAAASync.Text = "N/A";
                        lblAAASync.Foreground = Brushes.DarkGray;
                    }
                    else
                    {
                        lblAAASync.Text = AAA_IsUp ? "UP" : "DN";
                        lblAAASync.Foreground = AAA_IsUp ? Brushes.Lime : Brushes.Red;
                    }
                }
                // AIQ SuperBands - show N/A if not available, otherwise show UP/DN
                if (lblSuperBands != null)
                {
                    if (!UseAIQSuperBands)
                    {
                        lblSuperBands.Text = "OFF";
                        lblSuperBands.Foreground = Brushes.Gray;
                    }
                    else if (!SB_Available)
                    {
                        lblSuperBands.Text = "N/A";
                        lblSuperBands.Foreground = Brushes.DarkGray;
                    }
                    else
                    {
                        lblSuperBands.Text = SB_IsUp ? "UP" : "DN";
                        lblSuperBands.Foreground = SB_IsUp ? Brushes.Lime : Brushes.Red;
                    }
                }
                
                if (lblAIQ1Status != null)
                {
                    lblAIQ1Status.Text = AIQ1_IsUp ? "UP" : "DN";
                    lblAIQ1Status.Foreground = AIQ1_IsUp ? Brushes.Lime : Brushes.Red;
                }
                
                if (lblWindowStatus != null)
                {
                    bool inCooldown = false;
                    string cooldownText = "";
                    
                    if (UseTimeBasedCooldown && lastSignalTime != DateTime.MinValue)
                    {
                        double secondsSinceSignal = (DateTime.Now - lastSignalTime).TotalSeconds;
                        inCooldown = secondsSinceSignal < CooldownSeconds;
                        if (inCooldown)
                            cooldownText = $"🕐 Cooldown ({secondsSinceSignal:F0}s/{CooldownSeconds}s)";
                    }
                    else
                    {
                        inCooldown = CooldownBars > 0 && barsSinceLastSignal >= 0 && barsSinceLastSignal < CooldownBars;
                        if (inCooldown)
                            cooldownText = $"🕐 Cooldown ({barsSinceLastSignal}/{CooldownBars})";
                    }
                    
                    if (inCooldown)
                    {
                        lblWindowStatus.Text = cooldownText;
                        lblWindowStatus.Foreground = Brushes.Yellow;
                    }
                    else if (barsSinceYellowSquare >= 0 && barsSinceYellowSquare <= MaxBarsAfterYellowSquare)
                    {
                        lblWindowStatus.Text = $"⚡ LONG Window ({barsSinceYellowSquare}/{MaxBarsAfterYellowSquare})";
                        lblWindowStatus.Foreground = Brushes.Lime;
                    }
                    else if (barsSinceOrangeSquare >= 0 && barsSinceOrangeSquare <= MaxBarsAfterYellowSquare)
                    {
                        lblWindowStatus.Text = $"⚡ SHORT Window ({barsSinceOrangeSquare}/{MaxBarsAfterYellowSquare})";
                        lblWindowStatus.Foreground = Brushes.Orange;
                    }
                    else
                    {
                        lblWindowStatus.Text = "Window: CLOSED";
                        lblWindowStatus.Foreground = Brushes.Gray;
                    }
                }

                var (bull, bear, total) = GetConfluence();
                string dailyPnLText = (EnableDailyLossLimit || EnableDailyProfitTarget) ? $" | Day: ${dailyPnL:F0}" : "";
                string limitHitText = dailyLossLimitHit ? " 🛑STOPPED" : (dailyProfitTargetHit ? " 🎯TARGET" : "");
                if (lblSessionStats != null) lblSessionStats.Text = $"Signals: {signalCount} | Bull:{bull} Bear:{bear}/{total}{dailyPnLText}{limitHitText}";

                if (lblLastSignal != null && signalBorder != null)
                {
                    bool longWindowOpen = barsSinceYellowSquare >= 0 && barsSinceYellowSquare <= MaxBarsAfterYellowSquare;
                    bool shortWindowOpen = barsSinceOrangeSquare >= 0 && barsSinceOrangeSquare <= MaxBarsAfterYellowSquare;
                    
                    if (total == 0)
                    {
                        lblLastSignal.Text = "No indicators selected";
                        lblLastSignal.Foreground = Brushes.Gray;
                        lblLastSignal.FontWeight = FontWeights.Normal;
                        signalBorder.BorderBrush = Brushes.Transparent;
                        signalBorder.Background = Brushes.Transparent;
                    }
                    else if (longWindowOpen && RR_IsUp && bull >= MinConfluenceRequired)
                    {
                        lblLastSignal.Text = $"🔔 READY: LONG ({bull}/{total})";
                        lblLastSignal.FontWeight = FontWeights.Bold;
                        lblLastSignal.Foreground = Brushes.Lime;
                        signalBorder.BorderBrush = Brushes.Lime;
                        signalBorder.Background = new SolidColorBrush(Color.FromArgb(60, 0, 255, 0));
                    }
                    else if (shortWindowOpen && !RR_IsUp && bear >= MinConfluenceRequired)
                    {
                        lblLastSignal.Text = $"🔔 READY: SHORT ({bear}/{total})";
                        lblLastSignal.FontWeight = FontWeights.Bold;
                        lblLastSignal.Foreground = Brushes.Orange;
                        signalBorder.BorderBrush = Brushes.Orange;
                        signalBorder.Background = new SolidColorBrush(Color.FromArgb(60, 255, 165, 0));
                    }
                    else if (longWindowOpen)
                    {
                        string waiting = !RR_IsUp ? "RR not UP" : $"Bull {bull}/{MinConfluenceRequired}";
                        lblLastSignal.Text = $"LONG window - {waiting}";
                        lblLastSignal.FontWeight = FontWeights.Normal;
                        lblLastSignal.Foreground = Brushes.Yellow;
                        signalBorder.BorderBrush = Brushes.Yellow;
                        signalBorder.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 0));
                    }
                    else if (shortWindowOpen)
                    {
                        string waiting = RR_IsUp ? "RR not DN" : $"Bear {bear}/{MinConfluenceRequired}";
                        lblLastSignal.Text = $"SHORT window - {waiting}";
                        lblLastSignal.FontWeight = FontWeights.Normal;
                        lblLastSignal.Foreground = Brushes.Orange;
                        signalBorder.BorderBrush = Brushes.Orange;
                        signalBorder.Background = new SolidColorBrush(Color.FromArgb(30, 255, 165, 0));
                    }
                    else if (bull >= MinConfluenceRequired)
                    {
                        lblLastSignal.Text = $"Bull OK ({bull}/{total})\nWaiting for Yellow ■...";
                        lblLastSignal.FontWeight = FontWeights.Normal;
                        lblLastSignal.Foreground = Brushes.Lime;
                        signalBorder.BorderBrush = Brushes.Lime;
                        signalBorder.Background = Brushes.Transparent;
                    }
                    else if (bear >= MinConfluenceRequired)
                    {
                        lblLastSignal.Text = $"Bear OK ({bear}/{total})\nWaiting for Orange ■...";
                        lblLastSignal.FontWeight = FontWeights.Normal;
                        lblLastSignal.Foreground = Brushes.Orange;
                        signalBorder.BorderBrush = Brushes.Orange;
                        signalBorder.Background = Brushes.Transparent;
                    }
                    else
                    {
                        lblLastSignal.Text = $"Low confluence (Bull:{bull} Bear:{bear})";
                        lblLastSignal.FontWeight = FontWeights.Normal;
                        lblLastSignal.Foreground = Brushes.Gray;
                        signalBorder.BorderBrush = Brushes.Gray;
                        signalBorder.Background = Brushes.Transparent;
                    }
                }
            });
        }
        
        private void UpdateSignalDisplay(string trigger, int confluenceCount, int total, DateTime t, bool isLong)
        {
            signalCount++;
            string dir = isLong ? "LONG" : "SHORT";
            lastSignalText = $"{dir} @ {confluenceCount}/{total} [{trigger}] {t:HH:mm:ss}";
            if (EnableSoundAlert)
                try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
        }
        
        private void UpdLbl(TextBlock l, bool? v, bool en)
        {
            if (l == null) return;
            if (!en) { l.Text = "OFF"; l.Foreground = Brushes.Gray; }
            else if (!v.HasValue) { l.Text = "MIX"; l.Foreground = Brushes.Yellow; }
            else { l.Text = v.Value ? "UP" : "DN"; l.Foreground = v.Value ? Brushes.Lime : Brushes.Red; }
        }
        #endregion
    }
}
