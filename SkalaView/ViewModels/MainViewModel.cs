using SkalaView.ApiService;

namespace SkalaView.ViewModels;

public sealed class MainViewModel
{
    public MainViewModel()
    {
        var binanceChartApiService = new BinanceChartApiService();
        var binanceTickerApiService = new BinanceTickerApiService();
        var binanceIndicatorApiService = new BinanceIndicatorApiService();
        var binanceOrderBookApiService = new BinanceOrderBookApiService();
        var userWatchlistApiService = new UserWatchlistApiService();

        Shared = new SharedViewModel();
        TickerMenu = new TickerMenuViewModel(Shared, binanceTickerApiService, userWatchlistApiService);
        ToolbarMenu = new ToolbarMenuViewModel(Shared);
        Chart = new CandlestickChartViewModel(Shared, binanceChartApiService);
        IndicatorMenu = new IndicatorMenuViewModel(Shared, binanceIndicatorApiService);
        OrderBook = new OrderBookViewModel(Shared, binanceOrderBookApiService);
    }

    public SharedViewModel Shared { get; }

    public TickerMenuViewModel TickerMenu { get; }

    public ToolbarMenuViewModel ToolbarMenu { get; }

    public CandlestickChartViewModel Chart { get; }

    public IndicatorMenuViewModel IndicatorMenu { get; }

    public OrderBookViewModel OrderBook { get; }
}
