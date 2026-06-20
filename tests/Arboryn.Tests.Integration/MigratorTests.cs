using Arboryn.Infrastructure.Database;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class MigratorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public MigratorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"Arboryn-test-{Guid.NewGuid():N}.db");
        // Pooling=False : sans cela, Microsoft.Data.Sqlite garde le handle ouvert
        // après Dispose() (la connexion est rendue au pool), ce qui empêche la
        // suppression du fichier dans Dispose() sur Windows.
        _connectionString = $"Data Source={_dbPath};Pooling=False";
    }

    [Fact]
    public async Task ApplyMigrations_CreatesSchemaFromScratch()
    {
        var migrator = new Migrator(_connectionString, NullLogger<Migrator>.Instance);
        await migrator.ApplyMigrationsAsync();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var version = await connection.QuerySingleAsync<int>(
            "SELECT MAX(version) FROM schema_versions");
        version.Should().Be(2);

        var volumeCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM volumes WHERE id = '00000000-0000-0000-0000-000000000000'");
        volumeCount.Should().Be(1);

        // Migration 002 : la table des candidats d'enrichissement est présente.
        var candidateTable = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'enrichment_candidates'");
        candidateTable.Should().Be(1);
    }

    [Fact]
    public async Task ApplyMigrations_IsIdempotent()
    {
        var migrator = new Migrator(_connectionString, NullLogger<Migrator>.Instance);
        await migrator.ApplyMigrationsAsync();
        await migrator.ApplyMigrationsAsync(); // doit être no-op

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var versionCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM schema_versions");
        versionCount.Should().Be(2);
    }

    public void Dispose()
    {
        // Filet de sécurité : vide les pools au cas où une connexion poolée
        // subsisterait, puis supprime la base et ses fichiers annexes WAL.
        SqliteConnection.ClearAllPools();
        TryDelete(_dbPath);
        TryDelete(_dbPath + "-wal");
        TryDelete(_dbPath + "-shm");
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
            // Fichier temporaire : nettoyage best-effort, on ignore les verrous transitoires.
        }
    }
}
