using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using SkalaView.ApiService;
using LiveChartsCore.Defaults;

namespace SkalaView.ViewModels;

public class CandlestickChartViewModel : INotifyPropertyChanged
{
    private readonly SharedViewModel? _shared;
    private readonly BinanceChartApiService _binanceChartApiService;
    private CancellationTokenSource? _refreshCancellation;
    private string _loadStatus = "Loading data...";

    public CandlestickChartViewModel()
        : this(new BinanceChartApiService())
    {
    }

    public CandlestickChartViewModel(BinanceChartApiService binanceChartApiService)
    {
        _binanceChartApiService = binanceChartApiService;
        StartRefresh("BTC", "1m");
    }

    public CandlestickChartViewModel(SharedViewModel shared)
        : this(shared, new BinanceChartApiService())
    {
    }

    public CandlestickChartViewModel(SharedViewModel shared, BinanceChartApiService binanceChartApiService)
    {
        _shared = shared;
        _binanceChartApiService = binanceChartApiService;
        _shared.PropertyChanged += OnSharedPropertyChanged;

        StartRefresh(
            _shared.SelectedTicker?.Symbol ?? "BTC",
            _shared.SelectedTimeframe);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action? DataReloaded;

    public ObservableCollection<FinancialPoint> Values { get; } = new();

    public ObservableCollection<ChartCandlePoint> Candles { get; } = new();

    public string LoadStatus
    {
        get => _loadStatus;
        private set
        {
            if (_loadStatus == value) return;
            _loadStatus = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoadStatus)));
        }
    }

    public Func<DateTime, string> DateFormatter { get; set; }
        = value => value.ToString("dd MMM HH:mm", CultureInfo.InvariantCulture);

    private void OnSharedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SharedViewModel.SelectedTicker) &&
            e.PropertyName != nameof(SharedViewModel.SelectedTimeframe))
        {
            return;
        }

        StartRefresh(
            _shared?.SelectedTicker?.Symbol ?? "BTC",
            _shared?.SelectedTimeframe ?? "1m");
    }

    private void StartRefresh(string symbol, string timeframe)
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();
        var cancellationToken = _refreshCancellation.Token;

        _ = Task.Run(async () =>
        {
            await LoadMarketDataAsync(symbol, timeframe, cancellationToken, showLoading: true);

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(GetDelayUntilNextCandle(timeframe), cancellationToken);
                await LoadMarketDataAsync(symbol, timeframe, cancellationToken, showLoading: false);
            }
        }, cancellationToken);
    }

    private async Task LoadMarketDataAsync(
        string symbol,
        string timeframe,
        CancellationToken cancellationToken,
        bool showLoading)
    {
        try
        {
            if (showLoading)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoadStatus = $"Loading {symbol.ToUpperInvariant()} {timeframe}...";
                });
            }

            var marketData = await _binanceChartApiService.GetCandlesAsync(symbol, timeframe, cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Values.Clear();
                Candles.Clear();

                foreach (var candle in marketData.Candles)
                {
                    Candles.Add(new ChartCandlePoint(
                        candle.Time.DateTime,
                        candle.Open,
                        candle.High,
                        candle.Low,
                        candle.Close,
                        candle.Volume));

                    Values.Add(new FinancialPoint
                    {
                        Date = candle.Time.DateTime,
                        Open = candle.Open,
                        High = candle.High,
                        Low = candle.Low,
                        Close = candle.Close
                    });
                }

                LoadStatus = string.Empty;
                DataReloaded?.Invoke();
            });

            Console.WriteLine($"Loaded {marketData.Candles.Count} candles: {marketData.ApiSymbol} {marketData.ApiTimeframe}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoadStatus = $"Data could not be loaded: {ex.Message}";
            });

            Console.WriteLine($"Market data load failed for {symbol} {timeframe}: {ex.Message}");
        }
    }

    private static TimeSpan GetDelayUntilNextCandle(string timeframe)
    {
        var interval = GetTimeframeInterval(timeframe);
        var now = DateTimeOffset.UtcNow;
        var elapsedTicks = now.Ticks % interval.Ticks;
        var remainingTicks = elapsedTicks == 0
            ? interval.Ticks
            : interval.Ticks - elapsedTicks;

        return TimeSpan.FromTicks(remainingTicks) + TimeSpan.FromSeconds(1);
    }

    private static TimeSpan GetTimeframeInterval(string timeframe)
    {
        return timeframe.Trim().ToLowerInvariant() switch
        {
            "1m" or "1" or "1min" or "1minute" => TimeSpan.FromMinutes(1),
            "5m" or "5" or "5min" or "5minute" => TimeSpan.FromMinutes(5),
            "15m" or "15" or "15min" or "15minute" => TimeSpan.FromMinutes(15),
            "30m" or "30" or "30min" or "30minute" => TimeSpan.FromMinutes(30),
            "1h" or "1hour" => TimeSpan.FromHours(1),
            "4h" or "4hour" => TimeSpan.FromHours(4),
            "1d" or "1day" => TimeSpan.FromDays(1),
            _ => TimeSpan.FromMinutes(1)
        };
    }
}

public sealed record ChartCandlePoint(
    DateTime Date,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume);
