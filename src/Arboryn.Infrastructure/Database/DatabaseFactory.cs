using Dapper;
using Microsoft.Data.Sqlite;

namespace Arboryn.Infrastructure.Database;

/// <summary>
/// Fournit des connexions SQLite avec les PRAGMAs Arboryn appliqués.
/// </summary>
public sealed class DatabaseFactory
{
    private readonly string _connectionString;

    public DatabaseFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Ces PRAGMAs doivent être appliqués à chaque connexion (sauf journal_mode
        // qui est persistant, mais le poser ne coûte rien)
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON;").ConfigureAwait(false);
        await connection.ExecuteAsync("PRAGMA journal_mode = WAL;").ConfigureAwait(false);
        await connection.ExecuteAsync("PRAGMA synchronous = NORMAL;").ConfigureAwait(false);
        await connection.ExecuteAsync("PRAGMA temp_store = MEMORY;").ConfigureAwait(false);
        // Encaisse un checkpoint WAL ou une micro-latence réseau (base sur clé USB / partage)
        // sans échouer immédiatement sur « database is locked » (Inc 13, A2).
        await connection.ExecuteAsync("PRAGMA busy_timeout = 5000;").ConfigureAwait(false);

        return connection;
    }

    public string ConnectionString => _connectionString;
}
