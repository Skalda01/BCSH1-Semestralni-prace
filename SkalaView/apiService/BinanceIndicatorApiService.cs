using System;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SkalaView.ApiService;

public sealed class BinanceIndicatorApiService
{
    public async Task WatchTickerStatsAsync(
        string symbol,
        Func<BinanceIndicatorStats, Task> onStats,
        CancellationToken cancellationToken)
    {
        var apiSymbol = ToApiSymbol(symbol).ToLowerInvariant();
        var streamUri = new Uri($"wss://stream.binance.com:9443/ws/{apiSymbol}@ticker");

        using var webSocket = new ClientWebSocket();
        await webSocket.ConnectAsync(streamUri, cancellationToken);

        var buffer = new byte[32 * 1024];

        while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
        {
            var message = await ReceiveMessageAsync(webSocket, buffer, cancellationToken);
            if (string.IsNullOrWhiteSpace(message)) continue;

            var stats = ParseStats(message);
            await onStats(stats);
        }
    }

    private static async Task<string> ReceiveMessageAsync(
        ClientWebSocket webSocket,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        WebSocketReceiveResult result;

        do
        {
            result = await webSocket.ReceiveAsync(buffer, cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
                return string.Empty;

            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
        while (!result.EndOfMessage);

        return builder.ToString();
    }

    private static BinanceIndicatorStats ParseStats(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new BinanceIndicatorStats(
            root.GetProperty("s").GetString() ?? string.Empty,
            ParseDecimalString(root.GetProperty("p").GetString()),
            ParseDecimalString(root.GetProperty("P").GetString()),
            ParseDecimalString(root.GetProperty("w").GetString()),
            ParseDecimalString(root.GetProperty("c").GetString()),
            ParseDecimalString(root.GetProperty("h").GetString()),
            ParseDecimalString(root.GetProperty("l").GetString()),
            ParseDecimalString(root.GetProperty("v").GetString()),
            ParseDecimalString(root.GetProperty("q").GetString()),
            root.GetProperty("n").GetInt64(),
            ParseDecimalString(root.GetProperty("o").GetString()));
    }

    private static string ToApiSymbol(string symbol)
    {
        var value = symbol.Trim().ToUpperInvariant();
        return value.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
            ? value
            : $"{value}USDT";
    }

    private static double ParseDecimalString(string? value)
    {
        return double.Parse(value ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

public sealed record BinanceIndicatorStats(
    string Symbol,
    double PriceChange,
    double PriceChangePercent,
    double WeightedAveragePrice,
    double LastPrice,
    double HighPrice,
    double LowPrice,
    double Volume,
    double QuoteVolume,
    long TradeCount,
    double OpenPrice);
