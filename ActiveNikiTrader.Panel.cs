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
using System.Windows.Input;
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
        #region Resize Edge Enum
        private enum ResizeEdge
        {
            None,
            Left,
            Right,
            Top,
            Bottom,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }
        #endregion

        #region Additional Panel Fields
        private ResizeEdge currentResizeEdge = ResizeEdge.None;
        private const double EdgeThreshold = 8;  // pixels from edge to trigger resize
        private double panelWidth = 200;
        private double panelHeight = 400;
        private double minPanelWidth = 150;
        private double minPanelHeight = 200;
        private Point resizeStartMousePos;
        private double resizeStartLeft, resizeStartTop;
        #endregion

        #region Chart Panel
        private void CreateControlPanel()
        {
            try
            {
                string settingsDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8", "settings");
                panelSettingsFile = System.IO.Path.Combine(settingsDir, "ActiveNikiTrader_PanelSettings.txt");
                panelTransform = new TranslateTransform(0, 0);
                panelScale = new ScaleTransform(1, 1);
                
                LoadPanelSettings();

                controlPanel = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Background = new SolidColorBrush(Color.FromArgb(230, 30, 30, 40)),
                    Width = panelWidth,
                    MinWidth = minPanelWidth,
                    MinHeight = minPanelHeight,
                    RenderTransform = panelTransform,
                    Cursor = Cursors.Arrow
                };
                
                controlPanel.MouseLeftButtonDown += Panel_MouseLeftButtonDown;
                controlPanel.MouseLeftButtonUp += Panel_MouseLeftButtonUp;
                controlPanel.MouseMove += Panel_MouseMove;
                controlPanel.MouseLeave += Panel_MouseLeave;

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 100)),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(8)
                };
                
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = "ActiveNiki Trader", FontWeight = FontWeights.Bold, Foreground = Brushes.Cyan, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) });
                lblSubtitle = new TextBlock { Foreground = Brushes.LightGray, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,6) };
                stack.Children.Add(lblSubtitle);
                
                stack.Children.Add(new TextBlock { Text = "── Confluence (8) ──", Foreground = Brushes.Gray, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,2,0,2) });
                
                // Indicators in alphabetical order
                stack.Children.Add(CreateRow("AAA TrendSync", ref chkAAASync, ref lblAAASync, UseAAATrendSync));
                stack.Children.Add(CreateRow("AIQ SuperBands", ref chkSuperBands, ref lblSuperBands, UseAIQSuperBands));
                stack.Children.Add(CreateRow("Dragon Trend", ref chkDragonTrend, ref lblDragonTrend, UseDragonTrend));
                stack.Children.Add(CreateRow("Easy Trend", ref chkEasyTrend, ref lblEasyTrend, UseEasyTrend));
                stack.Children.Add(CreateRow("Ruby River", ref chkRubyRiver, ref lblRubyRiver, UseRubyRiver));
                stack.Children.Add(CreateRow("Solar Wave", ref chkSolarWave, ref lblSolarWave, UseSolarWave));
                stack.Children.Add(CreateRow("T3 Pro", ref chkT3Pro, ref lblT3Pro, UseT3Pro));
                stack.Children.Add(CreateRow("VIDYA Pro", ref chkVIDYA, ref lblVIDYA, UseVIDYAPro));
                
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
                
                // Add resize grip indicator in bottom-right corner
                var resizeIndicator = new Canvas { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 4, 0, 0) };
                for (int i = 0; i < 3; i++)
                {
                    var line = new Line { X1 = 10 - i * 4, Y1 = 10, X2 = 10, Y2 = 10 - i * 4, Stroke = new SolidColorBrush(Color.FromRgb(120, 120, 140)), StrokeThickness = 1 };
                    resizeIndicator.Children.Add(line);
                }
                stack.Children.Add(resizeIndicator);
                
                border.Child = stack;
                controlPanel.Children.Add(border);

                UIElementCollection panelHolder = (ChartControl.Parent as Grid)?.Children;
                if (panelHolder != null) panelHolder.Add(controlPanel);
                panelActive = true;
                
                // Apply initial position
                ApplyPanelConstraints();
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
                    controlPanel.MouseLeave -= Panel_MouseLeave;
                    UIElementCollection panelHolder = (ChartControl?.Parent as Grid)?.Children;
                    if (panelHolder != null && panelHolder.Contains(controlPanel))
                        panelHolder.Remove(controlPanel);
                    panelActive = false;
                }
            }
            catch { }
        }

        private ResizeEdge GetResizeEdge(Point mousePos)
        {
            double w = controlPanel.ActualWidth > 0 ? controlPanel.ActualWidth : panelWidth;
            double h = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : panelHeight;
            
            bool nearLeft = mousePos.X <= EdgeThreshold;
            bool nearRight = mousePos.X >= w - EdgeThreshold;
            bool nearTop = mousePos.Y <= EdgeThreshold;
            bool nearBottom = mousePos.Y >= h - EdgeThreshold;
            
            if (nearTop && nearLeft) return ResizeEdge.TopLeft;
            if (nearTop && nearRight) return ResizeEdge.TopRight;
            if (nearBottom && nearLeft) return ResizeEdge.BottomLeft;
            if (nearBottom && nearRight) return ResizeEdge.BottomRight;
            if (nearLeft) return ResizeEdge.Left;
            if (nearRight) return ResizeEdge.Right;
            if (nearTop) return ResizeEdge.Top;
            if (nearBottom) return ResizeEdge.Bottom;
            
            return ResizeEdge.None;
        }
        
        private Cursor GetCursorForEdge(ResizeEdge edge)
        {
            switch (edge)
            {
                case ResizeEdge.Left:
                case ResizeEdge.Right:
                    return Cursors.SizeWE;
                case ResizeEdge.Top:
                case ResizeEdge.Bottom:
                    return Cursors.SizeNS;
                case ResizeEdge.TopLeft:
                case ResizeEdge.BottomRight:
                    return Cursors.SizeNWSE;
                case ResizeEdge.TopRight:
                case ResizeEdge.BottomLeft:
                    return Cursors.SizeNESW;
                default:
                    return Cursors.Hand;
            }
        }

        private void Panel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point mousePos = e.GetPosition(controlPanel);
            ResizeEdge edge = GetResizeEdge(mousePos);
            
            if (edge != ResizeEdge.None)
            {
                // Start resizing
                currentResizeEdge = edge;
                isResizing = true;
                resizeStartMousePos = e.GetPosition(ChartControl?.Parent as UIElement);
                resizeStartWidth = controlPanel.ActualWidth > 0 ? controlPanel.ActualWidth : panelWidth;
                resizeStartHeight = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : panelHeight;
                resizeStartLeft = panelTransform.X;
                resizeStartTop = panelTransform.Y;
                controlPanel.CaptureMouse();
                e.Handled = true;
            }
            else
            {
                // Start dragging
                isDragging = true;
                dragStartPoint = e.GetPosition(ChartControl?.Parent as UIElement);
                dragStartPoint.X -= panelTransform.X;
                dragStartPoint.Y -= panelTransform.Y;
                controlPanel.CaptureMouse();
                e.Handled = true;
            }
        }

        private void Panel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isDragging || isResizing)
            {
                isDragging = false;
                isResizing = false;
                currentResizeEdge = ResizeEdge.None;
                controlPanel.ReleaseMouseCapture();
                SavePanelSettings();
                e.Handled = true;
            }
        }
        
        private void Panel_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!isDragging && !isResizing)
            {
                controlPanel.Cursor = Cursors.Arrow;
            }
        }

        private void Panel_MouseMove(object sender, MouseEventArgs e)
        {
            var parent = ChartControl?.Parent as FrameworkElement;
            if (parent == null) return;
            
            Point currentMousePos = e.GetPosition(parent);
            
            if (isResizing && currentResizeEdge != ResizeEdge.None)
            {
                double deltaX = currentMousePos.X - resizeStartMousePos.X;
                double deltaY = currentMousePos.Y - resizeStartMousePos.Y;
                
                double newWidth = resizeStartWidth;
                double newHeight = resizeStartHeight;
                double newLeft = resizeStartLeft;
                double newTop = resizeStartTop;
                
                // Calculate new dimensions based on which edge is being dragged
                switch (currentResizeEdge)
                {
                    case ResizeEdge.Right:
                        newWidth = resizeStartWidth + deltaX;
                        break;
                    case ResizeEdge.Left:
                        newWidth = resizeStartWidth - deltaX;
                        newLeft = resizeStartLeft + deltaX;
                        break;
                    case ResizeEdge.Bottom:
                        newHeight = resizeStartHeight + deltaY;
                        break;
                    case ResizeEdge.Top:
                        newHeight = resizeStartHeight - deltaY;
                        newTop = resizeStartTop + deltaY;
                        break;
                    case ResizeEdge.BottomRight:
                        // Proportional resize - maintain aspect ratio
                        double aspectRatio = resizeStartWidth / resizeStartHeight;
                        double avgDelta = (deltaX + deltaY) / 2;
                        newWidth = resizeStartWidth + avgDelta;
                        newHeight = newWidth / aspectRatio;
                        break;
                    case ResizeEdge.BottomLeft:
                        newWidth = resizeStartWidth - deltaX;
                        newLeft = resizeStartLeft + deltaX;
                        newHeight = resizeStartHeight + deltaY;
                        break;
                    case ResizeEdge.TopRight:
                        newWidth = resizeStartWidth + deltaX;
                        newHeight = resizeStartHeight - deltaY;
                        newTop = resizeStartTop + deltaY;
                        break;
                    case ResizeEdge.TopLeft:
                        newWidth = resizeStartWidth - deltaX;
                        newLeft = resizeStartLeft + deltaX;
                        newHeight = resizeStartHeight - deltaY;
                        newTop = resizeStartTop + deltaY;
                        break;
                }
                
                // Apply minimum size constraints
                if (newWidth < minPanelWidth)
                {
                    if (currentResizeEdge == ResizeEdge.Left || currentResizeEdge == ResizeEdge.TopLeft || currentResizeEdge == ResizeEdge.BottomLeft)
                        newLeft = resizeStartLeft + (resizeStartWidth - minPanelWidth);
                    newWidth = minPanelWidth;
                }
                if (newHeight < minPanelHeight)
                {
                    if (currentResizeEdge == ResizeEdge.Top || currentResizeEdge == ResizeEdge.TopLeft || currentResizeEdge == ResizeEdge.TopRight)
                        newTop = resizeStartTop + (resizeStartHeight - minPanelHeight);
                    newHeight = minPanelHeight;
                }
                
                // Apply boundary constraints
                if (newLeft < 0) 
                {
                    newWidth = newWidth + newLeft;  // Reduce width by the amount we went over
                    newLeft = 0;
                }
                if (newTop < 0)
                {
                    newHeight = newHeight + newTop;  // Reduce height by the amount we went over
                    newTop = 0;
                }
                if (newLeft + newWidth > parent.ActualWidth)
                {
                    newWidth = parent.ActualWidth - newLeft;
                }
                if (newTop + newHeight > parent.ActualHeight)
                {
                    newHeight = parent.ActualHeight - newTop;
                }
                
                // Re-apply minimum constraints after boundary adjustments
                newWidth = Math.Max(newWidth, minPanelWidth);
                newHeight = Math.Max(newHeight, minPanelHeight);
                
                // Apply changes
                panelWidth = newWidth;
                panelHeight = newHeight;
                controlPanel.Width = newWidth;
                controlPanel.Height = newHeight;
                panelTransform.X = newLeft;
                panelTransform.Y = newTop;
                
                e.Handled = true;
            }
            else if (isDragging)
            {
                double newX = currentMousePos.X - dragStartPoint.X;
                double newY = currentMousePos.Y - dragStartPoint.Y;
                
                double w = controlPanel.ActualWidth > 0 ? controlPanel.ActualWidth : panelWidth;
                double h = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : panelHeight;
                
                // Constrain to chart boundaries
                newX = Math.Max(0, Math.Min(parent.ActualWidth - w, newX));
                newY = Math.Max(0, Math.Min(parent.ActualHeight - h, newY));
                
                panelTransform.X = newX;
                panelTransform.Y = newY;
                e.Handled = true;
            }
            else
            {
                // Update cursor based on mouse position
                Point mousePos = e.GetPosition(controlPanel);
                ResizeEdge edge = GetResizeEdge(mousePos);
                controlPanel.Cursor = GetCursorForEdge(edge);
            }
        }

        private void ApplyPanelConstraints()
        {
            var parent = ChartControl?.Parent as FrameworkElement;
            if (parent == null || controlPanel == null) return;
            
            double maxX = Math.Max(0, parent.ActualWidth - panelWidth);
            double maxY = Math.Max(0, parent.ActualHeight - panelHeight);
            
            panelTransform.X = Math.Max(0, Math.Min(maxX, panelTransform.X));
            panelTransform.Y = Math.Max(0, Math.Min(maxY, panelTransform.Y));
            
            controlPanel.Width = panelWidth;
            controlPanel.Height = panelHeight;
        }

        private void SavePanelSettings()
        {
            try
            {
                if (string.IsNullOrEmpty(panelSettingsFile)) return;
                string dir = System.IO.Path.GetDirectoryName(panelSettingsFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                
                double w = controlPanel.ActualWidth > 0 ? controlPanel.ActualWidth : panelWidth;
                double h = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : panelHeight;
                
                // Save: X, Y, Width, Height
                File.WriteAllText(panelSettingsFile, $"{panelTransform.X},{panelTransform.Y},{w},{h}");
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
                {
                    panelTransform.X = x;
                    panelTransform.Y = y;
                }
                if (parts.Length >= 4 && double.TryParse(parts[2], out double w) && double.TryParse(parts[3], out double h))
                {
                    panelWidth = Math.Max(minPanelWidth, w);
                    panelHeight = Math.Max(minPanelHeight, h);
                }
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

                // Update labels (alphabetical order for consistency)
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
                UpdLbl(lblDragonTrend, DT_IsUp, UseDragonTrend);
                UpdLbl(lblEasyTrend, ET_IsUp, UseEasyTrend);
                UpdLbl(lblRubyRiver, RR_IsUp, UseRubyRiver);
                UpdLbl(lblSolarWave, SW_IsUp, UseSolarWave);
                UpdLbl(lblT3Pro, T3P_IsUp, UseT3Pro);
                UpdLbl(lblVIDYA, VY_IsUp, UseVIDYAPro);
                
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
