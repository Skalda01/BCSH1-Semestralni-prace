using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Controls.Shapes;
using SkalaView.ViewModels;
using LiveChartsCore.Defaults;

namespace SkalaView.AppComponent.ChartMenu;

public partial class ChartMenu : UserControl
{
    private const int MaxVisibleCandles = 200;
    private const int RightPaddingCandles = 20;
    private static readonly IBrush UpBrush = new SolidColorBrush(Color.Parse("#34D399"));
    private static readonly IBrush DownBrush = new SolidColorBrush(Color.Parse("#F87171"));
    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#263647"));

    private Border? _chart;
    private Canvas? _candleCanvas;
    private Canvas? _overlayCanvas;
    private Border? _crossVertical;
    private Border? _crossHorizontal;
    private TextBlock? _crossInfoText;
    private double? _maxVisibleXSpan;
    private (double Min, double Max)? _xRange;
    private (double Min, double Max)? _yRange;

    private Point? _panStartPoint;
    private (double Min, double Max)? _panStartXRange;
    private CandlestickChartViewModel? _boundVm;

    public ChartMenu()
    {
        InitializeComponent();
        AttachInteractions();
    }

    private void AttachInteractions()
    {
        _chart = this.FindControl<Border>("Chart");
        _candleCanvas = this.FindControl<Canvas>("CandleCanvas");
        _overlayCanvas = this.FindControl<Canvas>("OverlayCanvas");
        _crossVertical = this.FindControl<Border>("CrossVertical");
        _crossHorizontal = this.FindControl<Border>("CrossHorizontal");
        _crossInfoText = this.FindControl<TextBlock>("CrossInfoText");

        if (_chart is null || _candleCanvas is null) return;

        _chart.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnPointerWheelChanged,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        _chart.PointerPressed += OnPointerPressed;
        _chart.PointerMoved += OnPointerMoved;
        _chart.PointerReleased += OnPointerReleased;
        _chart.PointerExited += OnPointerExited;
        _chart.SizeChanged += (_, _) =>
        {
            RenderCandles();
            SnapCrosshairToLastCandle();
        };

        DataContextChanged += (_, _) =>
        {
            AttachVmHandlers();
            ApplyInitialViewport();
            RenderCandles();
            Dispatcher.UIThread.Post(SnapCrosshairToLastCandle);
        };
        Loaded += (_, _) =>
        {
            ApplyInitialViewport();
            RenderCandles();
            Dispatcher.UIThread.Post(SnapCrosshairToLastCandle);
        };
    }

    private void AttachVmHandlers()
    {
        if (_boundVm is not null)
            _boundVm.DataReloaded -= OnVmDataReloaded;

        _boundVm = DataContext as CandlestickChartViewModel;

        if (_boundVm is not null)
            _boundVm.DataReloaded += OnVmDataReloaded;
    }

    private void OnVmDataReloaded()
    {
        ApplyInitialViewport();
        RenderCandles();
        SnapCrosshairToLastCandle();
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_chart is null) return;

        var zoomIn = e.Delta.Y > 0;
        var factor = zoomIn ? 0.88 : 1.12;
        var mouse = e.GetPosition(_chart);
        var width = Math.Max(1, _chart.Bounds.Width);

        var xAnchor = mouse.X / width;

        var xRange = GetXRange();

        var zoomedX = ZoomRange(xRange, factor, xAnchor);
        var clampedX = ClampXRangeToMaxVisible(zoomedX);
        SetAxisRange(clampedX);
        ApplyAutoScaleYForVisibleX(clampedX);

        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_chart is null) return;

        if (e.ClickCount == 2)
        {
            ResetZoom();
            e.Handled = true;
            return;
        }

        var point = e.GetCurrentPoint(_chart);
        if (!point.Properties.IsRightButtonPressed) return;

        _panStartPoint = point.Position;
        _panStartXRange = GetXRange();
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_chart is null) return;

        var point = e.GetCurrentPoint(_chart);
        UpdateCrosshair(point.Position);

        if (_panStartPoint is null || _panStartXRange is null) return;
        if (!point.Properties.IsRightButtonPressed) return;

        var dx = point.Position.X - _panStartPoint.Value.X;
        var width = Math.Max(1, _chart.Bounds.Width);
        var xSpan = _panStartXRange.Value.Max - _panStartXRange.Value.Min;
        var xShift = -(dx / width) * xSpan;

        var shiftedX = (_panStartXRange.Value.Min + xShift, _panStartXRange.Value.Max + xShift);
        SetAxisRange(shiftedX);
        ApplyAutoScaleYForVisibleX(shiftedX);

        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _panStartPoint = null;
        _panStartXRange = null;
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        SnapCrosshairToLastCandle();
    }

    private (double Min, double Max) GetXRange()
    {
        if (_xRange.HasValue)
            return _xRange.Value;

        if (DataContext is CandlestickChartViewModel vm && vm.Values.Count > 0)
        {
            var min = vm.Values.Min(v => (double)v.Date.Ticks);
            var max = vm.Values.Max(v => (double)v.Date.Ticks);
            return ExpandIfFlat(min, max);
        }

        return (0, 1);
    }

    private void ResetZoom()
    {
        ApplyInitialViewport();
    }

    private void ApplyInitialViewport()
    {
        if (DataContext is not CandlestickChartViewModel vm || vm.Values.Count == 0) return;

        var count = vm.Values.Count;
        if (count <= 1)
        {
            _xRange = null;
            _yRange = null;
            _maxVisibleXSpan = null;
            RenderCandles();
            return;
        }

        var step = EstimateCandleStep(vm);
        var last = (double)vm.Values[count - 1].Date.Ticks;

        var max = last + (step * RightPaddingCandles);
        var min = max - (step * MaxVisibleCandles);

        _maxVisibleXSpan = Math.Max(1e-9, max - min);
        _xRange = (min, max);
        ApplyAutoScaleYForVisibleX((min, max));
    }

    private (double Min, double Max) ClampXRangeToMaxVisible((double Min, double Max) range)
    {
        if (!_maxVisibleXSpan.HasValue) return range;

        var span = range.Max - range.Min;
        if (span <= _maxVisibleXSpan.Value) return range;

        var center = (range.Min + range.Max) * 0.5;
        var half = _maxVisibleXSpan.Value * 0.5;
        return (center - half, center + half);
    }

    private static double EstimateCandleStep(CandlestickChartViewModel vm)
    {
        if (vm.Values.Count < 2) return TimeSpan.FromHours(1).Ticks;

        var last = vm.Values[vm.Values.Count - 1].Date.Ticks;
        var prev = vm.Values[vm.Values.Count - 2].Date.Ticks;
        var step = Math.Abs(last - prev);
        return Math.Max(1, step);
    }

    private void ApplyAutoScaleYForVisibleX((double Min, double Max)? xRange = null)
    {
        if (DataContext is not CandlestickChartViewModel vm || vm.Values.Count == 0) return;

        var xr = xRange ?? GetXRange();
        var visible = vm.Values
            .Where(v =>
            {
                var t = (double)v.Date.Ticks;
                return t >= xr.Min && t <= xr.Max;
            })
            .ToList();

        if (visible.Count == 0)
            visible = vm.Values.ToList();

        var min = visible.Min(v => v.Low);
        var max = visible.Max(v => v.High);
        var span = Math.Max(1e-9, max - min);
        var padding = span * 0.06;

        _yRange = (min - padding, max + padding);
        RenderCandles();
    }

    private void RenderCandles()
    {
        if (_chart is null || _candleCanvas is null) return;
        _candleCanvas.Children.Clear();

        if (DataContext is not CandlestickChartViewModel vm || vm.Values.Count == 0) return;

        var width = Math.Max(1, _chart.Bounds.Width);
        var height = Math.Max(1, _chart.Bounds.Height);
        _candleCanvas.Width = width;
        _candleCanvas.Height = height;

        var xRange = GetXRange();
        var yRange = _yRange;
        if (!yRange.HasValue) return;

        DrawGrid(width, height);

        var visible = vm.Values
            .Where(v =>
            {
                var ticks = (double)v.Date.Ticks;
                return ticks >= xRange.Min && ticks <= xRange.Max;
            })
            .ToList();

        if (visible.Count == 0) return;

        var candleSpacing = EstimateVisibleCandleSpacing(visible, xRange, width);
        var candleWidth = Math.Clamp(candleSpacing * 0.48, 1, 8);
        var drawBodies = candleSpacing >= 2.4;

        foreach (var point in visible)
        {
            var x = ToX(point.Date.Ticks, xRange, width);
            var openY = ToY(point.Open, yRange.Value, height);
            var closeY = ToY(point.Close, yRange.Value, height);
            var highY = ToY(point.High, yRange.Value, height);
            var lowY = ToY(point.Low, yRange.Value, height);
            var brush = point.Close >= point.Open ? UpBrush : DownBrush;

            _candleCanvas.Children.Add(new Line
            {
                StartPoint = new Point(x, highY),
                EndPoint = new Point(x, lowY),
                Stroke = brush,
                StrokeThickness = 1
            });

            if (!drawBodies)
            {
                continue;
            }

            var top = Math.Min(openY, closeY);
            var bodyHeight = Math.Max(1.5, Math.Abs(closeY - openY));
            var body = new Rectangle
            {
                Width = candleWidth,
                Height = bodyHeight,
                Fill = brush,
                Stroke = brush,
                StrokeThickness = 0,
                RadiusX = candleWidth >= 3 ? 1 : 0,
                RadiusY = candleWidth >= 3 ? 1 : 0
            };

            Canvas.SetLeft(body, x - (candleWidth / 2));
            Canvas.SetTop(body, top);
            _candleCanvas.Children.Add(body);
        }
    }

    private static double EstimateVisibleCandleSpacing(
        System.Collections.Generic.IReadOnlyList<FinancialPoint> visible,
        (double Min, double Max) xRange,
        double width)
    {
        if (visible.Count < 2) return 8;

        var minSpacing = double.MaxValue;
        var previousX = ToX(visible[0].Date.Ticks, xRange, width);

        for (var i = 1; i < visible.Count; i++)
        {
            var x = ToX(visible[i].Date.Ticks, xRange, width);
            var spacing = Math.Abs(x - previousX);
            if (spacing > 0.01)
                minSpacing = Math.Min(minSpacing, spacing);
            previousX = x;
        }

        return double.IsFinite(minSpacing) && minSpacing != double.MaxValue
            ? minSpacing
            : width / Math.Max(visible.Count, 1);
    }

    private void DrawGrid(double width, double height)
    {
        if (_candleCanvas is null) return;

        for (var i = 1; i <= 3; i++)
        {
            var y = height * i / 4d;
            _candleCanvas.Children.Add(new Line
            {
                StartPoint = new Point(0, y),
                EndPoint = new Point(width, y),
                Stroke = GridBrush,
                StrokeThickness = 1,
                Opacity = 0.55
            });
        }

        for (var i = 1; i <= 4; i++)
        {
            var x = width * i / 5d;
            _candleCanvas.Children.Add(new Line
            {
                StartPoint = new Point(x, 0),
                EndPoint = new Point(x, height),
                Stroke = GridBrush,
                StrokeThickness = 1,
                Opacity = 0.35
            });
        }
    }

    private static double ToX(double ticks, (double Min, double Max) range, double width)
    {
        return ((ticks - range.Min) / Math.Max(1e-9, range.Max - range.Min)) * width;
    }

    private static double ToY(double price, (double Min, double Max) range, double height)
    {
        return ((range.Max - price) / Math.Max(1e-9, range.Max - range.Min)) * height;
    }

    private void UpdateCrosshair(Point position)
    {
        if (_chart is null || _overlayCanvas is null || _crossVertical is null || _crossHorizontal is null || _crossInfoText is null)
            return;
        if (DataContext is not CandlestickChartViewModel vm || vm.Values.Count == 0)
            return;

        var width = Math.Max(1, _chart.Bounds.Width);
        var height = Math.Max(1, _chart.Bounds.Height);

        var x = Math.Clamp(position.X, 0, width);
        var y = Math.Clamp(position.Y, 0, height);

        _crossVertical.IsVisible = true;
        _crossHorizontal.IsVisible = true;
        _crossVertical.Height = height;
        _crossHorizontal.Width = width;
        Canvas.SetLeft(_crossVertical, x);
        Canvas.SetTop(_crossVertical, 0);
        Canvas.SetLeft(_crossHorizontal, 0);
        Canvas.SetTop(_crossHorizontal, y);

        var xRange = GetXRange();
        var yRange = _yRange;
        var xTicks = xRange.Min + ((x / width) * (xRange.Max - xRange.Min));
        var price = yRange.HasValue
            ? yRange.Value.Max - ((y / height) * (yRange.Value.Max - yRange.Value.Min))
            : 0d;

        var nearest = vm.Values
            .OrderBy(v => Math.Abs(v.Date.Ticks - xTicks))
            .First();

        _crossInfoText.Text =
            $"Date: {nearest.Date:yyyy-MM-dd HH:mm}  Price: {price.ToString("0.##", CultureInfo.InvariantCulture)}\n" +
            $"O: {nearest.Open.ToString("0.##", CultureInfo.InvariantCulture)}  " +
            $"H: {nearest.High.ToString("0.##", CultureInfo.InvariantCulture)}  " +
            $"L: {nearest.Low.ToString("0.##", CultureInfo.InvariantCulture)}  " +
            $"C: {nearest.Close.ToString("0.##", CultureInfo.InvariantCulture)}";
    }

    private void SnapCrosshairToLastCandle()
    {
        if (_chart is null || _overlayCanvas is null || _crossVertical is null || _crossHorizontal is null || _crossInfoText is null)
            return;
        if (DataContext is not CandlestickChartViewModel vm || vm.Values.Count == 0)
            return;

        var width = Math.Max(1, _chart.Bounds.Width);
        var height = Math.Max(1, _chart.Bounds.Height);
        var xRange = GetXRange();
        var yRange = _yRange;
        if (!yRange.HasValue) return;

        var last = vm.Values[vm.Values.Count - 1];
        var xTicks = (double)last.Date.Ticks;
        var x = ((xTicks - xRange.Min) / Math.Max(1e-9, xRange.Max - xRange.Min)) * width;
        x = Math.Clamp(x, 0, width);

        var yPrice = last.Close;
        var y = ToY(yPrice, yRange.Value, height);
        y = Math.Clamp(y, 0, height);

        _crossVertical.IsVisible = true;
        _crossHorizontal.IsVisible = true;
        _crossVertical.Height = height;
        _crossHorizontal.Width = width;
        Canvas.SetLeft(_crossVertical, x);
        Canvas.SetTop(_crossVertical, 0);
        Canvas.SetLeft(_crossHorizontal, 0);
        Canvas.SetTop(_crossHorizontal, y);

        _crossInfoText.Text =
            $"Date: {last.Date:yyyy-MM-dd HH:mm}  Price: {last.Close.ToString("0.##", CultureInfo.InvariantCulture)}\n" +
            $"O: {last.Open.ToString("0.##", CultureInfo.InvariantCulture)}  " +
            $"H: {last.High.ToString("0.##", CultureInfo.InvariantCulture)}  " +
            $"L: {last.Low.ToString("0.##", CultureInfo.InvariantCulture)}  " +
            $"C: {last.Close.ToString("0.##", CultureInfo.InvariantCulture)}";
    }
    

    private static (double Min, double Max) ZoomRange((double Min, double Max) range, double factor, double anchorRatio)
    {
        var span = Math.Max(1e-9, range.Max - range.Min);
        var newSpan = span * factor;
        var anchor = range.Min + (span * anchorRatio);
        var min = anchor - (newSpan * anchorRatio);
        var max = min + newSpan;
        return ExpandIfFlat(min, max);
    }

    private static (double Min, double Max) ExpandIfFlat(double min, double max)
    {
        if (Math.Abs(max - min) > 1e-9) return (min, max);
        return (min - 1, max + 1);
    }

    private void SetAxisRange((double Min, double Max) range)
    {
        _xRange = range;
        RenderCandles();
    }
}
