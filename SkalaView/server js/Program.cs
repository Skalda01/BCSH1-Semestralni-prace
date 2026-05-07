using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var webDatabasePath =
    Environment.GetEnvironmentVariable("WEB_DATABASE_PATH") ??
    Environment.GetEnvironmentVariable("DATABASE_PATH") ??
    "/var/www/html/app.sqlite";
var watchlistDatabasePath =
    Environment.GetEnvironmentVariable("WATCHLIST_DATABASE_PATH") ??
    "watchlist.sqlite";

var webConnectionString = new SqliteConnectionStringBuilder
{
    DataSource = webDatabasePath
}.ToString();
var watchlistConnectionString = new SqliteConnectionStringBuilder
{
    DataSource = watchlistDatabasePath
}.ToString();

var app = builder.Build();
app.UseCors();
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (UnauthorizedAccessException ex)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

EnsureWebDatabaseColumns(webConnectionString);
InitializeWatchlistDatabase(watchlistConnectionString);

app.MapPost("/api/app/validate-token", async (HttpRequest request) =>
{
    var auth = await AuthenticateAsync(request, webConnectionString);
    var watchlist = await GetWatchlistAsync(watchlistConnectionString, auth.TokenHash);

    return Results.Ok(new
    {
        valid = true,
        userId = auth.UserId,
        email = auth.Email,
        keyName = auth.KeyName,
        watchlist
    });
});

app.MapGet("/api/app/watchlist", async (HttpRequest request) =>
{
    var auth = await AuthenticateAsync(request, webConnectionString);
    var watchlist = await GetWatchlistAsync(watchlistConnectionString, auth.TokenHash);
    return Results.Ok(new { watchlist });
});

app.MapPost("/api/app/watchlist", async (HttpRequest request, WatchlistTickerRequest payload) =>
{
    var auth = await AuthenticateAsync(request, webConnectionString);
    var symbol = NormalizeSymbol(payload.Symbol);

    if (string.IsNullOrWhiteSpace(symbol))
        return Results.BadRequest(new { error = "Symbol je povinny." });

    await using var connection = new SqliteConnection(watchlistConnectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT OR IGNORE INTO watchlist_tickers (token_hash, symbol)
        VALUES ($tokenHash, $symbol)
    """;
    command.Parameters.AddWithValue("$tokenHash", auth.TokenHash);
    command.Parameters.AddWithValue("$symbol", symbol);
    await command.ExecuteNonQueryAsync();

    var watchlist = await GetWatchlistAsync(watchlistConnectionString, auth.TokenHash);
    return Results.Ok(new { watchlist });
});

app.MapDelete("/api/app/watchlist/{symbol}", async (HttpRequest request, string symbol) =>
{
    var auth = await AuthenticateAsync(request, webConnectionString);
    var normalizedSymbol = NormalizeSymbol(symbol);

    await using var connection = new SqliteConnection(watchlistConnectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
        DELETE FROM watchlist_tickers
        WHERE token_hash = $tokenHash AND symbol = $symbol
    """;
    command.Parameters.AddWithValue("$tokenHash", auth.TokenHash);
    command.Parameters.AddWithValue("$symbol", normalizedSymbol);
    await command.ExecuteNonQueryAsync();

    var watchlist = await GetWatchlistAsync(watchlistConnectionString, auth.TokenHash);
    return Results.Ok(new { watchlist });
});

app.Run("http://0.0.0.0:5050");

static void EnsureWebDatabaseColumns(string connectionString)
{
    using var connection = new SqliteConnection(connectionString);
    connection.Open();

    EnsureColumn(connection, "api_keys", "last_used_at", "TEXT");
    EnsureColumn(connection, "api_keys", "revoked_at", "TEXT");
}

static void InitializeWatchlistDatabase(string connectionString)
{
    using var connection = new SqliteConnection(connectionString);
    connection.Open();

    using var command = connection.CreateCommand();
    command.CommandText = """
        PRAGMA journal_mode = WAL;

        CREATE TABLE IF NOT EXISTS watchlist_tickers (
          token_hash TEXT NOT NULL,
          symbol TEXT NOT NULL,
          created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
          PRIMARY KEY (token_hash, symbol)
        );
    """;
    command.ExecuteNonQuery();
}

static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string columnType)
{
    using var checkCommand = connection.CreateCommand();
    checkCommand.CommandText = $"PRAGMA table_info({tableName})";

    using var reader = checkCommand.ExecuteReader();
    while (reader.Read())
    {
        if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            return;
    }

    using var alterCommand = connection.CreateCommand();
    alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType}";
    alterCommand.ExecuteNonQuery();
}

static async Task<AuthenticatedApiKey> AuthenticateAsync(HttpRequest request, string connectionString)
{
    var token = GetBearerToken(request);
    if (string.IsNullOrWhiteSpace(token))
        throw new UnauthorizedAccessException("Chybi API token.");

    var tokenHash = HashToken(token);

    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT api_keys.id, api_keys.user_id, api_keys.name, users.email
        FROM api_keys
        JOIN users ON users.id = api_keys.user_id
        WHERE api_keys.key_hash = $tokenHash AND api_keys.revoked_at IS NULL
    """;
    command.Parameters.AddWithValue("$tokenHash", tokenHash);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        throw new UnauthorizedAccessException("API token je neplatny nebo zruseny.");

    var keyId = reader.GetInt64(0);
    var userId = reader.GetInt64(1);
    var keyName = reader.GetString(2);
    var email = reader.GetString(3);

    await reader.DisposeAsync();

    await using var updateCommand = connection.CreateCommand();
    updateCommand.CommandText = "UPDATE api_keys SET last_used_at = CURRENT_TIMESTAMP WHERE id = $id";
    updateCommand.Parameters.AddWithValue("$id", keyId);
    await updateCommand.ExecuteNonQueryAsync();

    return new AuthenticatedApiKey(userId, email, keyName, tokenHash);
}

static async Task<IReadOnlyList<string>> GetWatchlistAsync(string connectionString, string tokenHash)
{
    var symbols = new List<string>();

    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT symbol
        FROM watchlist_tickers
        WHERE token_hash = $tokenHash
        ORDER BY created_at, symbol
    """;
    command.Parameters.AddWithValue("$tokenHash", tokenHash);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        symbols.Add(reader.GetString(0));
    }

    return symbols;
}

static string GetBearerToken(HttpRequest request)
{
    var auth = request.Headers.Authorization.ToString();
    return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? auth[7..].Trim()
        : string.Empty;
}

static string HashToken(string token)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

static string NormalizeSymbol(string? symbol)
{
    var value = (symbol ?? string.Empty).Trim().ToUpperInvariant();
    return value.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
        ? value[..^4]
        : value;
}

public sealed record WatchlistTickerRequest(string Symbol);

public sealed record AuthenticatedApiKey(long UserId, string Email, string KeyName, string TokenHash);
