using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SkalaView.ApiService;

public sealed class UserWatchlistApiService
{
    private const string ApiBaseUrl = "https://skalicky-test.cz/backend/api/app";
    private static readonly HttpClient HttpClient = new();

    public async Task<IReadOnlyList<string>> ValidateTokenAsync(string apiToken, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "validate-token", apiToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<WatchlistResponse>(
            cancellationToken: cancellationToken);

        return payload?.Watchlist ?? Array.Empty<string>();
    }

    public async Task<IReadOnlyList<string>> AddTickerAsync(
        string apiToken,
        string symbol,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "watchlist", apiToken);
        request.Content = JsonContent.Create(new { symbol });

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<WatchlistResponse>(
            cancellationToken: cancellationToken);

        return payload?.Watchlist ?? Array.Empty<string>();
    }

    public async Task<IReadOnlyList<string>> RemoveTickerAsync(
        string apiToken,
        string symbol,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Delete,
            $"watchlist/{Uri.EscapeDataString(symbol)}",
            apiToken);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<WatchlistResponse>(
            cancellationToken: cancellationToken);

        return payload?.Watchlist ?? Array.Empty<string>();
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string apiToken)
    {
        var request = new HttpRequestMessage(method, $"{ApiBaseUrl}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken.Trim());
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var message = $"Server returned {(int)response.StatusCode}.";

        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(
                cancellationToken: cancellationToken);

            if (!string.IsNullOrWhiteSpace(error?.Error))
                message = error.Error;
        }
        catch (JsonException)
        {
        }

        throw new InvalidOperationException(message);
    }

    private sealed record WatchlistResponse(IReadOnlyList<string> Watchlist);

    private sealed record ApiError(string Error);
}
