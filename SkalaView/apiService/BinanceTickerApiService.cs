using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SkalaView.ApiService;

public sealed class BinanceTickerApiService
{
    private const string ApiBaseUrl = "https://api.binance.com/api/v3/ticker/24hr";
    private static readonly HttpClient HttpClient = new();

    public async Task<IReadOnlyList<BinanceTickerSnapshot>> GetTickerSnapshotsAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<BinanceTickerSnapshot>();

        foreach (var symbol in symbols)
        {
            snapshots.Add(await GetTickerSnapshotAsync(symbol, cancellationToken));
        }

        return snapshots;
    }

    private static async Task<BinanceTickerSnapshot> GetTickerSnapshotAsync(
        string symbol,
        CancellationToken cancellationToken)
    {
        var apiSymbol = ToApiSymbol(symbol);
        var requestUrl = $"{ApiBaseUrl}?symbol={Uri.EscapeDataString(apiSymbol)}";

        using var response = await HttpClient.GetAsync(requestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new BinanceTickerSnapshot(
            ToDisplaySymbol(GetRequiredString(root, "symbol")),
            ParseDecimalString(GetRequiredString(root, "lastPrice")),
            ParseDecimalString(GetRequiredString(root, "priceChangePercent")),
            ParseDecimalString(GetRequiredString(root, "lowPrice")),
            ParseDecimalString(GetRequiredString(root, "highPrice")),
            ParseDecimalString(GetRequiredString(root, "volume")),
            ParseDecimalString(GetRequiredString(root, "quoteVolume")));
    }

    private static string ToApiSymbol(string symbol)
    {
        var value = symbol.Trim().ToUpperInvariant();
        return value.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
            ? value
            : $"{value}USDT";
    }

    private static string ToDisplaySymbol(string apiSymbol)
    {
        return apiSymbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
            ? apiSymbol[..^4]
            : apiSymbol;
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static double ParseDecimalString(string value)
    {
        return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

public sealed record BinanceTickerSnapshot(
    string Symbol,
    double LastPrice,
    double PriceChangePercent,
    double LowPrice,
    double HighPrice,
    double Volume,
    double QuoteVolume);
