using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using XinputG84HE.ViewModels;

namespace XinputG84HE;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainViewModel _viewModel;
    private bool _reallyClose;

    private int _draggingPointIndex = -1;
    private int _hoveredPointIndex = -1;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        _viewModel.OnCurveEditorRedrawNeeded += () => Dispatcher.Invoke(RenderGraph);
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsCurveEditorVisible) && _viewModel.IsCurveEditorVisible)
            {
                Dispatcher.InvokeAsync(RenderGraph);
            }
        };
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_reallyClose)
        {
            _viewModel.Shutdown();
            return;
        }

        // Minimize to tray instead of exiting - emulation should keep running
        // in the background even if the window is "closed".
        e.Cancel = true;
        Hide();
    }

    private void OnGraphCanvasSizeChanged(object sender, SizeChangedEventArgs e) => RenderGraph();

    private void RenderGraph()
    {
        if (GraphCanvas == null || !_viewModel.IsCurveEditorVisible || _viewModel.CurvePoints.Count < 11)
        {
            return;
        }

        GraphCanvas.Children.Clear();

        double width = GraphCanvas.ActualWidth;
        double height = GraphCanvas.ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        double leftMargin = 45;
        double rightMargin = 20;
        double topMargin = 20;
        double bottomMargin = 30;

        double plotWidth = Math.Max(10, width - leftMargin - rightMargin);
        double plotHeight = Math.Max(10, height - topMargin - bottomMargin);

        SolidColorBrush gridBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        SolidColorBrush textBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
        SolidColorBrush greenBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00C896"));
        SolidColorBrush greenGlowBrush = new SolidColorBrush(Color.FromArgb(0x44, 0x00, 0xC8, 0x96));

        // Grid lines & labels for Y-axis (Virtual Output %: 0%, 25%, 50%, 75%, 100%)
        for (int yStep = 0; yStep <= 4; yStep++)
        {
            double pct = yStep * 25;
            double py = topMargin + (1.0 - (pct / 100.0)) * plotHeight;

            Line gridLine = new Line
            {
                X1 = leftMargin,
                Y1 = py,
                X2 = leftMargin + plotWidth,
                Y2 = py,
                Stroke = gridBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 }
            };
            GraphCanvas.Children.Add(gridLine);

            TextBlock yLabel = new TextBlock
            {
                Text = $"{pct:0}%",
                FontSize = 10,
                Foreground = textBrush,
                TextAlignment = TextAlignment.Right,
                Width = 35
            };
            Canvas.SetLeft(yLabel, 5);
            Canvas.SetTop(yLabel, py - 7);
            GraphCanvas.Children.Add(yLabel);
        }

        // Grid lines & labels for X-axis (Physical Actuation %: 0%, 20%, 40%, 60%, 80%, 100%)
        for (int xStep = 0; xStep <= 5; xStep++)
        {
            double pct = xStep * 20;
            double px = leftMargin + (pct / 100.0) * plotWidth;

            Line gridLine = new Line
            {
                X1 = px,
                Y1 = topMargin,
                X2 = px,
                Y2 = topMargin + plotHeight,
                Stroke = gridBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 }
            };
            GraphCanvas.Children.Add(gridLine);

            TextBlock xLabel = new TextBlock
            {
                Text = $"{pct:0}%",
                FontSize = 10,
                Foreground = textBrush,
                TextAlignment = TextAlignment.Center,
                Width = 36
            };
            Canvas.SetLeft(xLabel, px - 18);
            Canvas.SetTop(xLabel, topMargin + plotHeight + 6);
            GraphCanvas.Children.Add(xLabel);
        }

        // 1:1 Default diagonal reference line (subtle dotted)
        Line diagonalRef = new Line
        {
            X1 = leftMargin,
            Y1 = topMargin + plotHeight,
            X2 = leftMargin + plotWidth,
            Y2 = topMargin,
            Stroke = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 4 }
        };
        GraphCanvas.Children.Add(diagonalRef);

        // Calculate point coordinates
        PointCollection points = new PointCollection();
        PointCollection polygonPoints = new PointCollection();

        // Polygon starts at bottom-left corner of plot
        polygonPoints.Add(new Point(leftMargin, topMargin + plotHeight));

        for (int i = 0; i < 11; i++)
        {
            double xPct = i * 10;
            double yPct = _viewModel.CurvePoints[i].Y;

            double px = leftMargin + (xPct / 100.0) * plotWidth;
            double py = topMargin + (1.0 - (Math.Clamp(yPct, 0, 100) / 100.0)) * plotHeight;

            Point pt = new Point(px, py);
            points.Add(pt);
            polygonPoints.Add(pt);
        }

        // Polygon ends at bottom-right corner of plot
        polygonPoints.Add(new Point(leftMargin + plotWidth, topMargin + plotHeight));

        // Filled area under curve with vertical gradient
        LinearGradientBrush fillGradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        fillGradient.GradientStops.Add(new GradientStop(Color.FromArgb(0x44, 0x00, 0xC8, 0x96), 0.0));
        fillGradient.GradientStops.Add(new GradientStop(Color.FromArgb(0x05, 0x00, 0xC8, 0x96), 1.0));

        Polygon filledArea = new Polygon
        {
            Points = polygonPoints,
            Fill = fillGradient
        };
        GraphCanvas.Children.Add(filledArea);

        // Solid curve polyline
        Polyline curvePolyline = new Polyline
        {
            Points = points,
            Stroke = greenBrush,
            StrokeThickness = 3,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        GraphCanvas.Children.Add(curvePolyline);

        // Draw interactive handles (11 control points)
        for (int i = 0; i < 11; i++)
        {
            Point pt = points[i];
            bool isHoveredOrDragged = (i == _hoveredPointIndex || i == _draggingPointIndex);

            if (isHoveredOrDragged)
            {
                Ellipse glow = new Ellipse
                {
                    Width = 24,
                    Height = 24,
                    Fill = greenGlowBrush,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(glow, pt.X - 12);
                Canvas.SetTop(glow, pt.Y - 12);
                GraphCanvas.Children.Add(glow);
            }

            Ellipse handle = new Ellipse
            {
                Width = isHoveredOrDragged ? 16 : 12,
                Height = isHoveredOrDragged ? 16 : 12,
                Fill = isHoveredOrDragged ? Brushes.White : greenBrush,
                Stroke = isHoveredOrDragged ? greenBrush : Brushes.White,
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(handle, pt.X - (isHoveredOrDragged ? 8 : 6));
            Canvas.SetTop(handle, pt.Y - (isHoveredOrDragged ? 8 : 6));
            GraphCanvas.Children.Add(handle);

            if (isHoveredOrDragged)
            {
                Border badge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x14, 0x17, 0x1E)),
                    BorderBrush = greenBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 3, 6, 3),
                    IsHitTestVisible = false
                };
                TextBlock badgeText = new TextBlock
                {
                    Text = $"{i * 10}% ➔ {_viewModel.CurvePoints[i].Y:0}%",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                };
                badge.Child = badgeText;

                Canvas.SetLeft(badge, Math.Clamp(pt.X - 35, 5, width - 80));
                Canvas.SetTop(badge, Math.Max(5, pt.Y - 32));
                GraphCanvas.Children.Add(badge);
            }
        }
    }

    private void OnGraphCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (GraphCanvas == null || _viewModel.CurvePoints.Count < 11) return;

        Point pt = e.GetPosition(GraphCanvas);
        double leftMargin = 45;
        double rightMargin = 20;
        double topMargin = 20;
        double bottomMargin = 30;

        double plotWidth = Math.Max(10, GraphCanvas.ActualWidth - leftMargin - rightMargin);
        double plotHeight = Math.Max(10, GraphCanvas.ActualHeight - topMargin - bottomMargin);

        // Find closest point by X coordinate
        double stepWidth = plotWidth / 10.0;
        int closestIndex = (int)Math.Round((pt.X - leftMargin) / stepWidth);
        closestIndex = Math.Clamp(closestIndex, 0, 10);

        _draggingPointIndex = closestIndex;
        _hoveredPointIndex = closestIndex;

        GraphCanvas.CaptureMouse();

        // Immediately update value to current click location Y
        double newY = (1.0 - (pt.Y - topMargin) / plotHeight) * 100.0;
        _viewModel.CurvePoints[closestIndex].Y = Math.Clamp(Math.Round(newY), 0, 100);

        RenderGraph();
    }

    private void OnGraphCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (GraphCanvas == null || _viewModel.CurvePoints.Count < 11) return;

        Point pt = e.GetPosition(GraphCanvas);
        double leftMargin = 45;
        double rightMargin = 20;
        double topMargin = 20;
        double bottomMargin = 30;

        double plotWidth = Math.Max(10, GraphCanvas.ActualWidth - leftMargin - rightMargin);
        double plotHeight = Math.Max(10, GraphCanvas.ActualHeight - topMargin - bottomMargin);

        if (_draggingPointIndex >= 0 && _draggingPointIndex < 11)
        {
            double newY = (1.0 - (pt.Y - topMargin) / plotHeight) * 100.0;
            _viewModel.CurvePoints[_draggingPointIndex].Y = Math.Clamp(Math.Round(newY), 0, 100);
            RenderGraph();
        }
        else
        {
            double stepWidth = plotWidth / 10.0;
            int closestIndex = (int)Math.Round((pt.X - leftMargin) / stepWidth);
            closestIndex = Math.Clamp(closestIndex, 0, 10);

            double targetPx = leftMargin + (closestIndex * stepWidth);
            double targetPy = topMargin + (1.0 - (_viewModel.CurvePoints[closestIndex].Y / 100.0)) * plotHeight;

            double dist = Math.Sqrt(Math.Pow(pt.X - targetPx, 2) + Math.Pow(pt.Y - targetPy, 2));
            int newHover = dist < 35 ? closestIndex : -1;

            if (newHover != _hoveredPointIndex)
            {
                _hoveredPointIndex = newHover;
                RenderGraph();
            }
        }
    }

    private void OnGraphCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingPointIndex >= 0)
        {
            _draggingPointIndex = -1;
            if (GraphCanvas != null && GraphCanvas.IsMouseCaptured)
            {
                GraphCanvas.ReleaseMouseCapture();
            }
            RenderGraph();
        }
    }

    private void OnGraphCanvasMouseLeave(object sender, MouseEventArgs e)
    {
        if (_draggingPointIndex < 0 && _hoveredPointIndex >= 0)
        {
            _hoveredPointIndex = -1;
            RenderGraph();
        }
    }

    private void OnPresetTileClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PresetTileViewModel tile })
        {
            tile.SelectCommand.Execute(null);
        }
    }

    private void OnPresetTileRightClick(object sender, MouseButtonEventArgs e)
    {
        // Reserved for a future right-click context menu.
    }

    private void OnPresetRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PresetTileViewModel tile })
        {
            if (e.Key == Key.Enter)
            {
                tile.CommitRenameCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                tile.CancelRenameCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void OnPresetRenameLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PresetTileViewModel tile } && tile.IsEditingName)
        {
            tile.CommitRenameCommand.Execute(null);
        }
    }

    private void OnCanvasRightClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsBinding)
        {
            _viewModel.CancelBindCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnTrayLeftClick(object sender, RoutedEventArgs e) => ShowWindow();

    private void OnTrayShow(object sender, RoutedEventArgs e) => ShowWindow();

    private void OnTrayToggleEmulation(object sender, RoutedEventArgs e) =>
        _viewModel.ToggleEmulationCommand.Execute(null);

    private void OnTrayExit(object sender, RoutedEventArgs e)
    {
        _reallyClose = true;
        TrayIcon.Dispose();
        Close();
        Application.Current.Shutdown();
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => OnTrayExit(sender, e);

    /// <summary>
    /// Redundant safety net alongside the IsChecked binding: explicitly pushes
    /// the toggle's new state into the view model the moment it's clicked,
    /// in case of any binding-timing edge case.
    /// </summary>
    private void OnBlockMappedKeysClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ToggleButton toggle)
        {
            _viewModel.BlockMappedKeys = toggle.IsChecked ?? false;
        }
    }

    private void OnSquareDeadzoneClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _viewModel.UseRadialDeadzone = false;
    }

    private void OnCircleDeadzoneClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _viewModel.UseRadialDeadzone = true;
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
