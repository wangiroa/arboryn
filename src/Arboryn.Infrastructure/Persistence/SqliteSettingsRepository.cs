using Arboryn.Application.Abstractions;
using Arboryn.Infrastructure.Database;
using Dapper;

namespace Arboryn.Infrastructure.Persistence;

/// <summary>Stockage clé-valeur sur la table <c>settings</c>.</summary>
public sealed class SqliteSettingsRepository : ISettingsRepository
{
    private readonly DatabaseFactory _databaseFactory;

    public SqliteSettingsRepository(DatabaseFactory databaseFactory)
        => _databaseFactory = databaseFactory;

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT value FROM settings WHERE key = @Key;",
            new { Key = key },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO settings (key, value, updated_at)
            VALUES (@Key, @Value, datetime('now'))
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = datetime('now');
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { Key = key, Value = value }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
