using Arboryn.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arboryn.Tests.Integration;

/// <summary>
/// Base SQLite jetable pour les tests d'intégration : crée un fichier temporaire,
/// applique les migrations, et nettoie (avec ses fichiers WAL) à la libération.
/// <c>Pooling=False</c> garantit que le handle est fermé dès le Dispose des connexions.
/// </summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _dbPath;

    public DatabaseFactory Factory { get; }

    private TestDatabase(string dbPath, string connectionString)
    {
        _dbPath = dbPath;
        Factory = new DatabaseFactory(connectionString);
    }

    public static async Task<TestDatabase> CreateAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"Arboryn-it-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath};Pooling=False";

        var migrator = new Migrator(connectionString, NullLogger<Migrator>.Instance);
        await migrator.ApplyMigrationsAsync();

        return new TestDatabase(dbPath, connectionString);
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        TryDelete(_dbPath);
        TryDelete(_dbPath + "-wal");
        TryDelete(_dbPath + "-shm");
        return ValueTask.CompletedTask;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Nettoyage best-effort.
        }
    }
}
