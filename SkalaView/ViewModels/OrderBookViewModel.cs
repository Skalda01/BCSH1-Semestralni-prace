using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using SkalaView.ApiService;

namespace SkalaView.ViewModels;

public sealed class OrderBookViewModel : INotifyPropertyChanged
{
    private readonly SharedViewModel _shared;
    private readonly BinanceOrderBookApiService _orderBookApiService;
    private CancellationTokenSource? _streamCancellation;
    private long _streamVersion;
    private string _symbolText = "Order Book";
    private string _statusText = "Waiting for ticker.";
    private string _spreadText = "Spread --";

    public OrderBookViewModel()
        : this(new SharedViewModel(), new BinanceOrderBookApiService())
    {
    }

    public OrderBookViewModel(SharedViewModel shared, BinanceOrderBookApiService orderBookApiService)
    {
        _shared = shared;
        _orderBookApiService = orderBookApiService;
        _shared.PropertyChanged += OnSharedPropertyChanged;
        StartStream(_shared.SelectedTicker?.Symbol);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<OrderBookLevel> Bids { get; } = new();

    public ObservableCollection<OrderBookLevel> Asks { get; } = new();

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

    public string SpreadText
    {
        get => _spreadText;
        private set => SetField(ref _spreadText, value);
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

        Bids.Clear();
        Asks.Clear();
        SymbolText = $"{streamSymbol} / USDT";
        StatusText = "Connecting depth stream...";
        SpreadText = "Spread --";

        _streamCancellation = new CancellationTokenSource();
        var cancellationToken = _streamCancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await _orderBookApiService.WatchOrderBookAsync(
                    streamSymbol,
                    snapshot => Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (streamVersion == _streamVersion)
                            ApplySnapshot(snapshot);
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
                        StatusText = $"Depth stream failed: {ex.Message}";
                });
            }
        }, cancellationToken);
    }

    private void ApplySnapshot(BinanceOrderBookSnapshot snapshot)
    {
        ReplaceLevels(Bids, snapshot.Bids.Take(10));
        ReplaceLevels(Asks, snapshot.Asks.Take(10));

        var bestBid = Bids.FirstOrDefault()?.Price;
        var bestAsk = Asks.FirstOrDefault()?.Price;

        if (bestBid.HasValue && bestAsk.HasValue)
        {
            var spread = bestAsk.Value - bestBid.Value;
            SpreadText = $"Spread {spread:N2}";
        }
        else
        {
            SpreadText = "Spread --";
        }

        StatusText = "Live depth";
    }

    private static void ReplaceLevels(
        ObservableCollection<OrderBookLevel> target,
        IEnumerable<OrderBookLevel> levels)
    {
        target.Clear();

        foreach (var level in levels)
        {
            target.Add(level);
        }
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

    private void SetField(ref string field, string value, [CallerMemberName] string propertyName = "")
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
