using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Database;
using Arboryn.Infrastructure.Persistence;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Arboryn.Tests.Integration;

/// <summary>
/// Partage sûr du catalogue (Inc 13, A2) : export via l'API Online Backup, import différé, et
/// PRAGMA busy_timeout appliqué.
/// </summary>
public class CatalogTransferTests
{
    [Fact]
    public async Task Export_ProducesConsistentCopy_WithSchemaAndRows()
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedVolumeAsync(db, "EXPORT01", "Disque export");
        var transfer = new SqliteCatalogTransfer(db.Factory, new DatabaseLocationInfo(TempDir(), db.DatabasePath));

        var dest = TempDbPath();
        try
        {
            await transfer.ExportAsync(dest, CancellationToken.None);

            File.Exists(dest).Should().BeTrue();
            await using var export = new SqliteConnection($"Data Source={dest};Pooling=False");
            await export.OpenAsync();
            var name = await export.ExecuteScalarAsync<string>(
                "SELECT name FROM volumes WHERE serial = 'EXPORT01';");
            name.Should().Be("Disque export");
            var maxVersion = await export.ExecuteScalarAsync<long>("SELECT MAX(version) FROM schema_versions;");
            maxVersion.Should().Be(3);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Cleanup(dest);
        }
    }

    [Fact]
    public async Task PendingImport_RestoresScheduledSource_AndClearsMarker()
    {
        // Source = un export propre d'une base contenant un volume repérable.
        await using var seed = await TestDatabase.CreateAsync();
        await SeedVolumeAsync(seed, "IMPORT42", "Base partagée");
        var source = TempDbPath();
        var arborynDir = TempDir();
        var target = TempDbPath();
        try
        {
            await new SqliteCatalogTransfer(seed.Factory, new DatabaseLocationInfo(arborynDir, seed.DatabasePath))
                .ExportAsync(source, CancellationToken.None);

            new SqliteCatalogTransfer(seed.Factory, new DatabaseLocationInfo(arborynDir, seed.DatabasePath))
                .ScheduleImport(source);

            var applied = PendingDatabaseImport.ApplyIfScheduled(arborynDir, target);

            applied.Should().BeTrue();
            File.Exists(Path.Combine(arborynDir, PendingDatabaseImport.MarkerFileName)).Should().BeFalse();
            await using var imported = new SqliteConnection($"Data Source={target};Pooling=False");
            await imported.OpenAsync();
            var name = await imported.ExecuteScalarAsync<string>(
                "SELECT name FROM volumes WHERE serial = 'IMPORT42';");
            name.Should().Be("Base partagée");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Cleanup(source);
            Cleanup(target);
            TryDeleteDir(arborynDir);
        }
    }

    [Fact]
    public void PendingImport_NoMarker_ReturnsFalse()
    {
        var arborynDir = TempDir();
        try
        {
            PendingDatabaseImport.ApplyIfScheduled(arborynDir, TempDbPath()).Should().BeFalse();
        }
        finally
        {
            TryDeleteDir(arborynDir);
        }
    }

    [Fact]
    public async Task BusyTimeout_IsConfiguredOnConnections()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var connection = await db.Factory.OpenAsync(CancellationToken.None);
        var timeout = await connection.ExecuteScalarAsync<long>("PRAGMA busy_timeout;");
        timeout.Should().Be(5000);
    }

    private static async Task SeedVolumeAsync(TestDatabase db, string serial, string name)
    {
        var repo = new SqliteVolumeRepository(db.Factory);
        await repo.UpsertAsync(
            new VolumeRecord(VolumeId.New(), name, VolumeKind.External, VolumeStatus.Online) { Serial = serial },
            CancellationToken.None);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"arboryn-ct-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string TempDbPath()
        => Path.Combine(Path.GetTempPath(), $"arboryn-ct-{Guid.NewGuid():N}.db");

    private static void Cleanup(string dbPath)
    {
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + ".arbtmp" })
        {
            try
            {
                if (File.Exists(p))
                {
                    File.Delete(p);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
