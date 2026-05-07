using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SkalaView.ApiService;

public sealed class BinanceOrderBookApiService
{
    public async Task WatchOrderBookAsync(
        string symbol,
        Func<BinanceOrderBookSnapshot, Task> onSnapshot,
        CancellationToken cancellationToken)
    {
        var apiSymbol = ToApiSymbol(symbol).ToLowerInvariant();
        var streamUri = new Uri($"wss://stream.binance.com:9443/ws/{apiSymbol}@depth20@100ms");

        using var webSocket = new ClientWebSocket();
        await webSocket.ConnectAsync(streamUri, cancellationToken);

        var buffer = new byte[64 * 1024];

        while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
        {
            var message = await ReceiveMessageAsync(webSocket, buffer, cancellationToken);
            if (string.IsNullOrWhiteSpace(message)) continue;

            var snapshot = ParseSnapshot(symbol, message);
            await onSnapshot(snapshot);
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

    private static BinanceOrderBookSnapshot ParseSnapshot(string symbol, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new BinanceOrderBookSnapshot(
            ToDisplaySymbol(symbol),
            ParseLevels(root.GetProperty("bids")),
            ParseLevels(root.GetProperty("asks")));
    }

    private static IReadOnlyList<OrderBookLevel> ParseLevels(JsonElement levels)
    {
        var result = new List<OrderBookLevel>();

        foreach (var level in levels.EnumerateArray())
        {
            if (level.ValueKind != JsonValueKind.Array || level.GetArrayLength() < 2)
                continue;

            result.Add(new OrderBookLevel(
                ParseDecimalString(level[0].GetString()),
                ParseDecimalString(level[1].GetString())));
        }

        return result;
    }

    private static string ToApiSymbol(string symbol)
    {
        var value = symbol.Trim().ToUpperInvariant();
        return value.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
            ? value
            : $"{value}USDT";
    }

    private static string ToDisplaySymbol(string symbol)
    {
        var value = symbol.Trim().ToUpperInvariant();
        return value.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
            ? value[..^4]
            : value;
    }

    private static double ParseDecimalString(string? value)
    {
        return double.Parse(value ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

public sealed record BinanceOrderBookSnapshot(
    string Symbol,
    IReadOnlyList<OrderBookLevel> Bids,
    IReadOnlyList<OrderBookLevel> Asks);

public sealed record OrderBookLevel(double Price, double Quantity)
{
    public double Total => Price * Quantity;

    public string PriceText => Price.ToString("N2", CultureInfo.InvariantCulture);

    public string QuantityText => Quantity.ToString("0.####", CultureInfo.InvariantCulture);

    public string TotalText => Total.ToString("N0", CultureInfo.InvariantCulture);
}
