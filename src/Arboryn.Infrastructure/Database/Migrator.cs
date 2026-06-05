using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Arboryn.Infrastructure.Database;

/// <summary>
/// Applique les migrations SQL au démarrage de l'application. Les fichiers
/// 0XX_Description.sql sont chargés depuis Database/Migrations/ (copiés en
/// sortie de build via CopyToOutputDirectory).
/// </summary>
public sealed class Migrator
{
    private readonly string _connectionString;
    private readonly ILogger<Migrator> _logger;

    public Migrator(string connectionString, ILogger<Migrator> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task ApplyMigrationsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // PRAGMAs à chaque ouverture — voir aussi DatabaseFactory
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON;").ConfigureAwait(false);
        await connection.ExecuteAsync("PRAGMA journal_mode = WAL;").ConfigureAwait(false);
        await connection.ExecuteAsync("PRAGMA synchronous = NORMAL;").ConfigureAwait(false);

        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS schema_versions (
                version     INTEGER PRIMARY KEY,
                applied_at  TEXT NOT NULL DEFAULT (datetime('now')),
                description TEXT
            );").ConfigureAwait(false);

        var currentVersion = await connection.QuerySingleAsync<int>(
            "SELECT COALESCE(MAX(version), 0) FROM schema_versions;").ConfigureAwait(false);

        _logger.LogInformation("Schema actuel : v{Version}", currentVersion);

        var migrationsDir = Path.Combine(AppContext.BaseDirectory, "Database", "Migrations");
        if (!Directory.Exists(migrationsDir))
        {
            _logger.LogWarning("Répertoire de migrations introuvable : {Path}", migrationsDir);
            return;
        }

        var migrationFiles = Directory.GetFiles(migrationsDir, "*.sql")
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToArray();

        foreach (var file in migrationFiles)
        {
            var fileName = Path.GetFileName(file);
            if (!TryParseMigrationVersion(fileName, out var version, out var description))
            {
                _logger.LogWarning("Fichier de migration ignoré (format invalide) : {FileName}", fileName);
                continue;
            }

            if (version <= currentVersion)
            {
                continue;
            }

            _logger.LogInformation("Application de la migration v{Version} : {Description}", version, description);

            var sql = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);

            using var transaction = connection.BeginTransaction();
            try
            {
                await connection.ExecuteAsync(sql, transaction: transaction).ConfigureAwait(false);
                // Note : la migration 001 insère elle-même son schema_versions.
                // Pour les suivantes, on insère ici :
                if (version != 1)
                {
                    await connection.ExecuteAsync(
                        "INSERT INTO schema_versions (version, description) VALUES (@version, @description);",
                        new { version, description },
                        transaction).ConfigureAwait(false);
                }
                transaction.Commit();
                _logger.LogInformation("Migration v{Version} appliquée", version);
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "Échec de la migration v{Version}", version);
                throw;
            }
        }
    }

    private static bool TryParseMigrationVersion(string fileName, out int version, out string description)
    {
        version = 0;
        description = string.Empty;

        // Format attendu : 0XX_Description.sql
        var match = System.Text.RegularExpressions.Regex.Match(fileName, @"^(\d+)_(.+)\.sql$");
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, out version))
        {
            return false;
        }

        description = match.Groups[2].Value.Replace('_', ' ');
        return true;
    }
}
