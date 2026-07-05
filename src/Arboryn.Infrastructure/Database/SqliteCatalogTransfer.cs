using Arboryn.Application.Abstractions;
using Microsoft.Data.Sqlite;

namespace Arboryn.Infrastructure.Database;

/// <summary>
/// Implémentation SQLite du partage sûr du catalogue (Inc 13, A2). L'export s'appuie sur l'API
/// Online Backup (<see cref="SqliteConnection.BackupDatabase"/>), sûre même avec un WAL actif sur
/// la base de travail. L'import est différé au prochain démarrage via <see cref="PendingDatabaseImport"/>.
/// </summary>
public sealed class SqliteCatalogTransfer : ICatalogTransfer
{
    private readonly DatabaseFactory _databaseFactory;
    private readonly DatabaseLocationInfo _location;

    public SqliteCatalogTransfer(DatabaseFactory databaseFactory, DatabaseLocationInfo location)
    {
        _databaseFactory = databaseFactory;
        _location = location;
    }

    public async Task ExportAsync(string destinationPath, CancellationToken cancellationToken)
    {
        var destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        // Écrit un fichier temporaire cohérent puis le déplace en place (atomique) pour ne jamais
        // laisser un export partiel à l'emplacement final.
        var tempPath = destinationPath + ".arbtmp";
        DeleteQuietly(tempPath);

        await using (var source = new SqliteConnection(_databaseFactory.ConnectionString))
        await using (var destination = new SqliteConnection($"Data Source={tempPath}"))
        {
            await source.OpenAsync(cancellationToken).ConfigureAwait(false);
            await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
            source.BackupDatabase(destination);
        }

        // SqliteConnection garde le handle par pooling : vide le pool avant le déplacement.
        SqliteConnection.ClearAllPools();
        File.Move(tempPath, destinationPath, overwrite: true);
    }

    public void ScheduleImport(string sourcePath)
        => PendingDatabaseImport.Schedule(_location.ArborynDir, sourcePath);

    private static void DeleteQuietly(string path)
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
            // best-effort
        }
    }
}
