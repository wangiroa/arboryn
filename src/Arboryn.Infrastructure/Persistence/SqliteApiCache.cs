using System.Globalization;
using Arboryn.Application.Abstractions;
using Arboryn.Infrastructure.Database;
using Dapper;

namespace Arboryn.Infrastructure.Persistence;

/// <summary>
/// Cache SQLite des réponses des providers (table <c>api_cache</c>, PK <c>(provider, query_hash)</c>).
/// Les entrées expirées sont ignorées à la lecture. Sert à éviter les appels réseau répétés et
/// à servir des résultats hors-ligne (mode local-only).
/// </summary>
public sealed class SqliteApiCache : IApiCache
{
    private readonly DatabaseFactory _databaseFactory;

    public SqliteApiCache(DatabaseFactory databaseFactory)
        => _databaseFactory = databaseFactory;

    public async Task<string?> GetAsync(string provider, string queryHash, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT response_json
            FROM api_cache
            WHERE provider = @Provider AND query_hash = @QueryHash
              AND (expires_at IS NULL OR expires_at > datetime('now'));
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            sql, new { Provider = provider, QueryHash = queryHash }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task SetAsync(
        string provider, string queryHash, string responseJson, TimeSpan? timeToLive, CancellationToken cancellationToken)
    {
        // expires_at = now + ttl, ou NULL si pas d'expiration. Le modificateur SQLite est
        // construit signé côté C# (« +86400 seconds » / « -1 seconds ») pour rester valide.
        const string sql = """
            INSERT INTO api_cache (provider, query_hash, response_json, cached_at, expires_at)
            VALUES (
                @Provider, @QueryHash, @ResponseJson, datetime('now'),
                CASE WHEN @Modifier IS NULL THEN NULL ELSE datetime('now', @Modifier) END)
            ON CONFLICT(provider, query_hash) DO UPDATE SET
                response_json = excluded.response_json,
                cached_at     = excluded.cached_at,
                expires_at    = excluded.expires_at;
            """;

        string? modifier = null;
        if (timeToLive is { } ttl)
        {
            var seconds = (long)ttl.TotalSeconds;
            var sign = seconds < 0 ? string.Empty : "+";
            modifier = $"{sign}{seconds.ToString(CultureInfo.InvariantCulture)} seconds";
        }

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Provider = provider, QueryHash = queryHash, ResponseJson = responseJson, Modifier = modifier },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
