using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using SkalaView.ApiService;
using SkalaView.AppComponent.TickerMenu;

namespace SkalaView.ViewModels;

public sealed class TickerMenuViewModel : INotifyPropertyChanged
{
    private readonly SharedViewModel _shared;
    private readonly BinanceTickerApiService _binanceTickerApiService;
    private readonly UserWatchlistApiService _userWatchlistApiService;
    private readonly SemaphoreSlim _tickerDataLock = new(1, 1);
    private CancellationTokenSource? _serverRequestCancellation;
    private CancellationTokenSource? _tickerRefreshCancellation;
    private string _apiToken = string.Empty;
    private string _connectedApiToken = string.Empty;
    private string _addTickerSymbol = string.Empty;
    private string _statusMessage = "Enter API token to load server watchlist.";

    public TickerMenuViewModel()
        : this(new SharedViewModel())
    {
    }

    public TickerMenuViewModel(SharedViewModel shared)
        : this(shared, new BinanceTickerApiService(), new UserWatchlistApiService())
    {
    }

    public TickerMenuViewModel(
        SharedViewModel shared,
        BinanceTickerApiService binanceTickerApiService,
        UserWatchlistApiService userWatchlistApiService)
    {
        _shared = shared;
        _binanceTickerApiService = binanceTickerApiService;
        _userWatchlistApiService = userWatchlistApiService;
        _shared.PropertyChanged += OnSharedPropertyChanged;
        ConnectCommand = new RelayCommand(_ => _ = ConnectAsync());
        AddTickerCommand = new RelayCommand(_ => _ = AddTickerAsync());
        RemoveSelectedTickerCommand = new RelayCommand(_ => _ = RemoveSelectedTickerAsync());

        ReplaceWatchlist(new[] { "BTC", "ETH", "SOL" });

        if (_shared.SelectedTicker is null)
            _shared.SelectedTicker = Tickers[0];

        StartTickerRefresh();
    }

    public ObservableCollection<TickerItem> Tickers { get; } = new();

    public ICommand ConnectCommand { get; }

    public ICommand AddTickerCommand { get; }

    public ICommand RemoveSelectedTickerCommand { get; }

    public string ApiToken
    {
        get => _apiToken;
        set
        {
            if (_apiToken == value) return;
            _apiToken = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApiToken)));
        }
    }

    public string AddTickerSymbol
    {
        get => _addTickerSymbol;
        set
        {
            if (_addTickerSymbol == value) return;
            _addTickerSymbol = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AddTickerSymbol)));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
        }
    }

    public TickerItem? SelectedTicker
    {
        get => _shared.SelectedTicker;
        set
        {
            if (_shared.SelectedTicker == value) return;
            _shared.SelectedTicker = value;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async Task ConnectAsync()
    {
        var token = ApiToken.Trim();

        if (string.IsNullOrWhiteSpace(token))
        {
            StatusMessage = "API token is required.";
            return;
        }

        await LoadServerWatchlistAsync(
            () => _userWatchlistApiService.ValidateTokenAsync(token, CreateCancellationToken()),
            token);
    }

    private async Task AddTickerAsync()
    {
        var symbol = NormalizeSymbol(AddTickerSymbol);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            StatusMessage = "Ticker symbol is required.";
            return;
        }

        var token = GetActiveApiToken();

        if (string.IsNullOrWhiteSpace(token))
        {
            StatusMessage = "Connect API token first.";
            return;
        }

        await LoadServerWatchlistAsync(() =>
            _userWatchlistApiService.AddTickerAsync(token, symbol, CreateCancellationToken()));

        AddTickerSymbol = string.Empty;
    }

    private async Task RemoveSelectedTickerAsync()
    {
        if (SelectedTicker is null)
        {
            StatusMessage = "Select ticker to remove.";
            return;
        }

        var token = GetActiveApiToken();

        if (string.IsNullOrWhiteSpace(token))
        {
            StatusMessage = "Connect API token first.";
            return;
        }

        await LoadServerWatchlistAsync(() =>
            _userWatchlistApiService.RemoveTickerAsync(token, SelectedTicker.Symbol, CreateCancellationToken()));
    }

    private async Task LoadServerWatchlistAsync(
        Func<Task<IReadOnlyList<string>>> load,
        string? successfulToken = null)
    {
        try
        {
            StatusMessage = "Loading server watchlist...";
            var symbols = await load();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ReplaceWatchlist(symbols);
                StatusMessage = $"Watchlist loaded: {Tickers.Count}";
            });

            if (!string.IsNullOrWhiteSpace(successfulToken))
            {
                _connectedApiToken = successfulToken;
                ApiToken = string.Empty;
            }

            await LoadTickerDataAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private string GetActiveApiToken()
    {
        return string.IsNullOrWhiteSpace(_connectedApiToken)
            ? ApiToken.Trim()
            : _connectedApiToken;
    }

    private void OnSharedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharedViewModel.SelectedTicker))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTicker)));
    }

    private CancellationToken CreateCancellationToken()
    {
        _serverRequestCancellation?.Cancel();
        _serverRequestCancellation?.Dispose();
        _serverRequestCancellation = new CancellationTokenSource();
        return _serverRequestCancellation.Token;
    }

    private void StartTickerRefresh()
    {
        _tickerRefreshCancellation?.Cancel();
        _tickerRefreshCancellation?.Dispose();
        _tickerRefreshCancellation = new CancellationTokenSource();
        var cancellationToken = _tickerRefreshCancellation.Token;

        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await LoadTickerDataAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }, cancellationToken);
    }

    private async Task LoadTickerDataAsync(CancellationToken cancellationToken)
    {
        if (!await _tickerDataLock.WaitAsync(0, cancellationToken))
            return;

        try
        {
            var symbols = await Dispatcher.UIThread.InvokeAsync(() =>
                Tickers.Select(ticker => ticker.Symbol).ToList());

            if (symbols.Count == 0) return;

            var snapshots = await _binanceTickerApiService.GetTickerSnapshotsAsync(
                symbols,
                cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var snapshot in snapshots)
                {
                    var ticker = Tickers.FirstOrDefault(item =>
                        string.Equals(item.Symbol, snapshot.Symbol, StringComparison.OrdinalIgnoreCase));

                    if (ticker is null) continue;

                    ticker.Price = snapshot.LastPrice;
                    ticker.Change = snapshot.PriceChangePercent;
                    ticker.DayLow = snapshot.LowPrice;
                    ticker.DayHigh = snapshot.HighPrice;
                    ticker.Volume = (long)Math.Round(snapshot.Volume);
                    ticker.MarketCap = snapshot.QuoteVolume;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Binance ticker data load failed: {ex.Message}");
        }
        finally
        {
            _tickerDataLock.Release();
        }
    }

    private void ReplaceWatchlist(IEnumerable<string> symbols)
    {
        var normalizedSymbols = symbols
            .Select(NormalizeSymbol)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Tickers.Clear();

        foreach (var symbol in normalizedSymbols)
        {
            Tickers.Add(CreateTickerItem(symbol));
        }

        SelectedTicker = Tickers.Count > 0 ? Tickers[0] : null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTicker)));
    }

    private static TickerItem CreateTickerItem(string symbol)
    {
        return new TickerItem
        {
            Symbol = symbol,
            DisplayName = GetDisplayName(symbol),
            Exchange = "BINANCE",
            Price = 0,
            Change = 0
        };
    }

    private static string NormalizeSymbol(string? symbol)
    {
        var value = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        return value.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
            ? value[..^4]
            : value;
    }

    private static string GetDisplayName(string symbol)
    {
        return symbol.ToUpperInvariant() switch
        {
            "BTC" => "Bitcoin",
            "ETH" => "Ethereum",
            "SOL" => "Solana",
            "BNB" => "BNB",
            "XRP" => "XRP",
            "ADA" => "Cardano",
            "DOGE" => "Dogecoin",
            "AVAX" => "Avalanche",
            "DOT" => "Polkadot",
            "LINK" => "Chainlink",
            var value => value
        };
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;

        public RelayCommand(Action<object?> execute)
        {
            _execute = execute;
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            _execute(parameter);
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }
}
