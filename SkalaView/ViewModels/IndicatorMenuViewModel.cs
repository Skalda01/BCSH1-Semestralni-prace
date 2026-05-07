using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using SkalaView.ApiService;

namespace SkalaView.ViewModels;

public sealed class IndicatorMenuViewModel : INotifyPropertyChanged
{
    private readonly SharedViewModel _shared;
    private readonly BinanceIndicatorApiService _indicatorApiService;
    private CancellationTokenSource? _streamCancellation;
    private long _streamVersion;
    private string _symbolText = "Ticker stats";
    private string _statusText = "Waiting for ticker.";
    private string _priceChangeText = "--";
    private string _priceChangePercentText = "--";
    private string _weightedAveragePriceText = "--";
    private string _lastPriceText = "--";
    private string _highPriceText = "--";
    private string _lowPriceText = "--";
    private string _volumeText = "--";
    private string _quoteVolumeText = "--";
    private string _tradeCountText = "--";
    private string _openPriceText = "--";
    private string _rangeText = "--";
    private double _rangePosition;
    private IBrush _changeBrush = Brushes.LightGray;

    public IndicatorMenuViewModel()
        : this(new SharedViewModel(), new BinanceIndicatorApiService())
    {
    }

    public IndicatorMenuViewModel(SharedViewModel shared, BinanceIndicatorApiService indicatorApiService)
    {
        _shared = shared;
        _indicatorApiService = indicatorApiService;
        _shared.PropertyChanged += OnSharedPropertyChanged;
        StartStream(_shared.SelectedTicker?.Symbol);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SymbolText
    {
        get => _symbolText;
        private set => SetField(ref _symbolText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string PriceChangeText
    {
        get => _priceChangeText;
        private set => SetField(ref _priceChangeText, value);
    }

    public string PriceChangePercentText
    {
        get => _priceChangePercentText;
        private set => SetField(ref _priceChangePercentText, value);
    }

    public string WeightedAveragePriceText
    {
        get => _weightedAveragePriceText;
        private set => SetField(ref _weightedAveragePriceText, value);
    }

    public string LastPriceText
    {
        get => _lastPriceText;
        private set => SetField(ref _lastPriceText, value);
    }

    public string HighPriceText
    {
        get => _highPriceText;
        private set => SetField(ref _highPriceText, value);
    }

    public string LowPriceText
    {
        get => _lowPriceText;
        private set => SetField(ref _lowPriceText, value);
    }

    public string VolumeText
    {
        get => _volumeText;
        private set => SetField(ref _volumeText, value);
    }

    public string QuoteVolumeText
    {
        get => _quoteVolumeText;
        private set => SetField(ref _quoteVolumeText, value);
    }

    public string TradeCountText
    {
        get => _tradeCountText;
        private set => SetField(ref _tradeCountText, value);
    }

    public string OpenPriceText
    {
        get => _openPriceText;
        private set => SetField(ref _openPriceText, value);
    }

    public string RangeText
    {
        get => _rangeText;
        private set => SetField(ref _rangeText, value);
    }

    public double RangePosition
    {
        get => _rangePosition;
        private set => SetField(ref _rangePosition, value);
    }

    public IBrush ChangeBrush
    {
        get => _changeBrush;
        private set => SetField(ref _changeBrush, value);
    }

    private void OnSharedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharedViewModel.SelectedTicker))
            StartStream(_shared.SelectedTicker?.Symbol);
    }

    private void StartStream(string? symbol)
    {
        var streamVersion = Interlocked.Increment(ref _streamVersion);
        var streamSymbol = NormalizeSymbol(symbol);

        _streamCancellation?.Cancel();
        ResetValues();

        SymbolText = $"{streamSymbol} / USDT";
        StatusText = "Connecting ticker stream...";

        _streamCancellation = new CancellationTokenSource();
        var cancellationToken = _streamCancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await _indicatorApiService.WatchTickerStatsAsync(
                    streamSymbol,
                    stats => Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (streamVersion == _streamVersion)
                            ApplyStats(stats);
                    }).GetTask(),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (streamVersion == _streamVersion)
                        StatusText = $"Ticker stream failed: {ex.Message}";
                });
            }
        }, cancellationToken);
    }

    private void ApplyStats(BinanceIndicatorStats stats)
    {
        SymbolText = stats.Symbol;
        StatusText = "Live ticker";
        PriceChangeText = FormatSignedPrice(stats.PriceChange);
        PriceChangePercentText = $"{FormatSigned(stats.PriceChangePercent)}%";
        WeightedAveragePriceText = FormatPrice(stats.WeightedAveragePrice);
        LastPriceText = FormatPrice(stats.LastPrice);
        HighPriceText = FormatPrice(stats.HighPrice);
        LowPriceText = FormatPrice(stats.LowPrice);
        VolumeText = FormatCompact(stats.Volume);
        QuoteVolumeText = FormatCompact(stats.QuoteVolume);
        TradeCountText = stats.TradeCount.ToString("N0", CultureInfo.InvariantCulture);
        OpenPriceText = FormatPrice(stats.OpenPrice);
        RangeText = $"{FormatPrice(stats.LowPrice)} - {FormatPrice(stats.HighPrice)}";
        RangePosition = CalculateRangePosition(stats.LastPrice, stats.LowPrice, stats.HighPrice);
        ChangeBrush = stats.PriceChange >= 0
            ? new SolidColorBrush(Color.Parse("#34D399"))
            : new SolidColorBrush(Color.Parse("#F87171"));
    }

    private void ResetValues()
    {
        PriceChangeText = "--";
        PriceChangePercentText = "--";
        WeightedAveragePriceText = "--";
        LastPriceText = "--";
        HighPriceText = "--";
        LowPriceText = "--";
        VolumeText = "--";
        QuoteVolumeText = "--";
        TradeCountText = "--";
        OpenPriceText = "--";
        RangeText = "--";
        RangePosition = 0;
        ChangeBrush = Brushes.LightGray;
    }

    private static double CalculateRangePosition(double lastPrice, double lowPrice, double highPrice)
    {
        var range = highPrice - lowPrice;
        if (range <= 0) return 0;

        var position = ((lastPrice - lowPrice) / range) * 100d;
        return Math.Clamp(position, 0, 100);
    }

    private static string FormatSigned(double value)
    {
        return value.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture);
    }

    private static string FormatSignedPrice(double value)
    {
        return value.ToString("+0.####;-0.####;0", CultureInfo.InvariantCulture);
    }

    private static string FormatPrice(double value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string FormatCompact(double value)
    {
        var abs = Math.Abs(value);

        if (abs >= 1_000_000_000)
            return $"{value / 1_000_000_000:0.##}B";

        if (abs >= 1_000_000)
            return $"{value / 1_000_000:0.##}M";

        if (abs >= 1_000)
            return $"{value / 1_000:0.##}K";

        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string NormalizeSymbol(string? symbol)
    {
        var value = (symbol ?? string.Empty).Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(value))
            return "BTC";

        return value.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
            ? value[..^4]
            : value;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
