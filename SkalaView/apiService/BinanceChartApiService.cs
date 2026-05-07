using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SkalaView.ApiService;

public sealed class BinanceChartApiService
{
    private const string ApiBaseUrl = "https://api.binance.com/api/v3/klines";
    private const int CandleCount = 300;
    private static readonly HttpClient HttpClient = new();

    public async Task<BinanceChartDataResponse> GetCandlesAsync(
        string symbol,
        string timeframe,
        CancellationToken cancellationToken)
    {
        var apiSymbol = ToApiSymbol(symbol);
        var apiInterval = NormalizeTimeframe(timeframe);
        var requestUrl = BuildRequestUrl(apiSymbol, apiInterval);

        using var response = await HttpClient.GetAsync(requestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var candles = ParseCandles(json)
            .OrderBy(candle => candle.Time)
            .ToList();

        if (candles.Count == 0)
            throw new InvalidOperationException("Binance returned an empty kline response.");

        return new BinanceChartDataResponse(apiSymbol, apiInterval, candles);
    }

    private static string BuildRequestUrl(string apiSymbol, string apiInterval)
    {
        var query =
            $"symbol={Uri.EscapeDataString(apiSymbol)}" +
            $"&interval={Uri.EscapeDataString(apiInterval)}" +
            $"&limit={CandleCount}";

        return $"{ApiBaseUrl}?{query}";
    }

    private static string ToApiSymbol(string symbol)
    {
        var value = symbol.Trim().ToUpperInvariant();
        return value.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
            ? value
            : $"{value}USDT";
    }

    private static string NormalizeTimeframe(string timeframe)
    {
        return timeframe.Trim() switch
        {
            "1" or "1min" or "1minute" => "1m",
            "5" or "5min" or "5minute" => "5m",
            "15" or "15min" or "15minute" => "15m",
            "30" or "30min" or "30minute" => "30m",
            "1hour" => "1h",
            "4hour" => "4h",
            "1day" => "1d",
            var value => value.ToLowerInvariant()
        };
    }

    private static IEnumerable<BinanceChartCandle> ParseCandles(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Array)
            return Array.Empty<BinanceChartCandle>();

        var candles = new List<BinanceChartCandle>();

        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 6)
                continue;

            candles.Add(new BinanceChartCandle(
                DateTimeOffset.FromUnixTimeMilliseconds(item[0].GetInt64()),
                ParseDecimalString(item[1].GetString()),
                ParseDecimalString(item[2].GetString()),
                ParseDecimalString(item[3].GetString()),
                ParseDecimalString(item[4].GetString()),
                ParseDecimalString(item[5].GetString())));
        }

        return candles;
    }

    private static double ParseDecimalString(string? value)
    {
        return double.Parse(value ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

public sealed record BinanceChartDataResponse(
    string ApiSymbol,
    string ApiTimeframe,
    IReadOnlyList<BinanceChartCandle> Candles);

public sealed record BinanceChartCandle(
    DateTimeOffset Time,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume);
