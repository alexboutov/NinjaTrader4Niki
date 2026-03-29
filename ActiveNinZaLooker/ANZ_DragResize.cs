using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class ActiveNinZaLooker : Indicator
    {
        #region Edge Detection

        private ResizeEdge GetEdge(Point p)
        {
            double w = controlPanel.ActualWidth  > 0 ? controlPanel.ActualWidth  : panelWidth;
            double h = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : panelHeight;
            bool L = p.X <= EdgeThreshold, R = p.X >= w - EdgeThreshold;
            bool T = p.Y <= EdgeThreshold, B = p.Y >= h - EdgeThreshold;
            if (T && L) return ResizeEdge.TopLeft;
            if (T && R) return ResizeEdge.TopRight;
            if (B && L) return ResizeEdge.BottomLeft;
            if (B && R) return ResizeEdge.BottomRight;
            if (L) return ResizeEdge.Left;
            if (R) return ResizeEdge.Right;
            if (T) return ResizeEdge.Top;
            if (B) return ResizeEdge.Bottom;
            return ResizeEdge.None;
        }

        private Cursor CursorFor(ResizeEdge e)
        {
            switch (e)
            {
                case ResizeEdge.Left:  case ResizeEdge.Right:          return Cursors.SizeWE;
                case ResizeEdge.Top:   case ResizeEdge.Bottom:         return Cursors.SizeNS;
                case ResizeEdge.TopLeft:    case ResizeEdge.BottomRight: return Cursors.SizeNWSE;
                case ResizeEdge.TopRight:   case ResizeEdge.BottomLeft:  return Cursors.SizeNESW;
                default: return Cursors.Hand;
            }
        }

        #endregion

        #region Mouse Handlers

        private void Panel_MouseDown(object s, MouseButtonEventArgs e)
        {
            var parent = ChartControl?.Parent as UIElement;
            var edge   = GetEdge(e.GetPosition(controlPanel));
            if (edge != ResizeEdge.None)
            {
                currentResizeEdge   = edge;
                isResizing          = true;
                resizeStartMousePos = e.GetPosition(parent);
                resizeStartWidth    = controlPanel.ActualWidth  > 0 ? controlPanel.ActualWidth  : panelWidth;
                resizeStartHeight   = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : panelHeight;
                resizeStartLeft     = panelTransform.X;
                resizeStartTop      = panelTransform.Y;
            }
            else
            {
                isDragging     = true;
                dragStartPoint = e.GetPosition(parent);
                dragStartPoint.X -= panelTransform.X;
                dragStartPoint.Y -= panelTransform.Y;
            }
            controlPanel.CaptureMouse();
            e.Handled = true;
        }

        private void Panel_MouseUp(object s, MouseButtonEventArgs e)
        {
            if (isDragging || isResizing)
            {
                isDragging = isResizing = false;
                currentResizeEdge = ResizeEdge.None;
                controlPanel.ReleaseMouseCapture();
                SavePanelSettings();
                e.Handled = true;
            }
        }

        private void Panel_MouseLeave(object s, MouseEventArgs e)
        {
            if (!isDragging && !isResizing)
                controlPanel.Cursor = Cursors.Arrow;
        }

        private void Panel_MouseMove(object s, MouseEventArgs e)
        {
            var parent = ChartControl?.Parent as FrameworkElement;
            if (parent == null) return;
            var cur = e.GetPosition(parent);

            if (isResizing)
            {
                double dx = cur.X - resizeStartMousePos.X;
                double dy = cur.Y - resizeStartMousePos.Y;
                double nW = resizeStartWidth, nH = resizeStartHeight;
                double nL = resizeStartLeft,  nT = resizeStartTop;

                switch (currentResizeEdge)
                {
                    case ResizeEdge.Right:       nW = resizeStartWidth  + dx; break;
                    case ResizeEdge.Left:        nW = resizeStartWidth  - dx; nL = resizeStartLeft + dx; break;
                    case ResizeEdge.Bottom:      nH = resizeStartHeight + dy; break;
                    case ResizeEdge.Top:         nH = resizeStartHeight - dy; nT = resizeStartTop  + dy; break;
                    case ResizeEdge.BottomRight: nW = resizeStartWidth  + dx; nH = resizeStartHeight + dy; break;
                    case ResizeEdge.BottomLeft:  nW = resizeStartWidth  - dx; nL = resizeStartLeft + dx; nH = resizeStartHeight + dy; break;
                    case ResizeEdge.TopRight:    nW = resizeStartWidth  + dx; nH = resizeStartHeight - dy; nT = resizeStartTop + dy; break;
                    case ResizeEdge.TopLeft:     nW = resizeStartWidth  - dx; nL = resizeStartLeft + dx; nH = resizeStartHeight - dy; nT = resizeStartTop + dy; break;
                }

                nW = Math.Max(minPanelWidth,  nW);
                nH = Math.Max(minPanelHeight, nH);
                nL = Math.Max(0, Math.Min(parent.ActualWidth  - nW, nL));
                nT = Math.Max(0, Math.Min(parent.ActualHeight - nH, nT));

                panelWidth  = nW; panelHeight = nH;
                controlPanel.Width  = nW;
                controlPanel.Height = nH;
                panelTransform.X = nL;
                panelTransform.Y = nT;
                e.Handled = true;
            }
            else if (isDragging)
            {
                double w = controlPanel.ActualWidth  > 0 ? controlPanel.ActualWidth  : panelWidth;
                double h = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : panelHeight;
                panelTransform.X = Math.Max(0, Math.Min(parent.ActualWidth  - w, cur.X - dragStartPoint.X));
                panelTransform.Y = Math.Max(0, Math.Min(parent.ActualHeight - h, cur.Y - dragStartPoint.Y));
                e.Handled = true;
            }
            else
            {
                controlPanel.Cursor = CursorFor(GetEdge(e.GetPosition(controlPanel)));
            }
        }

        #endregion

        #region Panel Constraints & Settings Persistence

        private void ApplyPanelConstraints()
        {
            var parent = ChartControl?.Parent as FrameworkElement;
            if (parent == null || controlPanel == null) return;
            panelTransform.X = Math.Max(0, Math.Min(parent.ActualWidth  - panelWidth,  panelTransform.X));
            panelTransform.Y = Math.Max(0, Math.Min(parent.ActualHeight - panelHeight, panelTransform.Y));
            controlPanel.Width  = panelWidth;
            controlPanel.Height = panelHeight;
        }

        private void SavePanelSettings()
        {
            try
            {
                if (string.IsNullOrEmpty(panelSettingsFile)) return;
                string dir = Path.GetDirectoryName(panelSettingsFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                double w = controlPanel.ActualWidth  > 0 ? controlPanel.ActualWidth  : panelWidth;
                double h = controlPanel.ActualHeight > 0 ? controlPanel.ActualHeight : panelHeight;
                File.WriteAllText(panelSettingsFile, $"{panelTransform.X},{panelTransform.Y},{w},{h}");
            }
            catch { }
        }

        private void LoadPanelSettings()
        {
            try
            {
                if (string.IsNullOrEmpty(panelSettingsFile) || !File.Exists(panelSettingsFile)) return;
                var parts = File.ReadAllText(panelSettingsFile).Split(',');
                if (parts.Length >= 2
                    && double.TryParse(parts[0], out double x)
                    && double.TryParse(parts[1], out double y))
                {
                    panelTransform.X = x;
                    panelTransform.Y = y;
                }
                if (parts.Length >= 4
                    && double.TryParse(parts[2], out double w)
                    && double.TryParse(parts[3], out double h))
                {
                    panelWidth  = Math.Max(minPanelWidth,  w);
                    panelHeight = Math.Max(minPanelHeight, h);
                }
            }
            catch { }
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
