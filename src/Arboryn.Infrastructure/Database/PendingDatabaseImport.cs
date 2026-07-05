using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Arboryn.Infrastructure.Database;

/// <summary>
/// Import différé du catalogue (Inc 13, A2). Importer remplace la base de travail par une base
/// partagée ; comme la base est ouverte en cours d'exécution, l'opération est <b>planifiée</b>
/// (un marqueur est écrit) puis appliquée au <b>prochain démarrage</b>, avant toute ouverture de
/// connexion. La copie passe par l'API SQLite Online Backup pour produire une base cohérente,
/// même si la source possède un journal WAL.
/// </summary>
public static class PendingDatabaseImport
{
    /// <summary>Nom du marqueur d'import en attente (par-machine, dans le dossier Arboryn).</summary>
    public const string MarkerFileName = "pending-import.json";

    /// <summary>Planifie l'import de <paramref name="sourcePath"/> : prend effet au redémarrage.</summary>
    public static void Schedule(string arborynDir, string sourcePath)
    {
        Directory.CreateDirectory(arborynDir);
        var path = Path.Combine(arborynDir, MarkerFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(new MarkerDto { Source = sourcePath }));
    }

    /// <summary>
    /// Applique un import planifié s'il existe : restaure la base source dans
    /// <paramref name="databasePath"/> puis retire le marqueur. Renvoie <c>true</c> si un import
    /// a été appliqué. À appeler au démarrage, <b>avant</b> toute ouverture de la base de travail.
    /// </summary>
    public static bool ApplyIfScheduled(string arborynDir, string databasePath)
    {
        var markerPath = Path.Combine(arborynDir, MarkerFileName);
        if (!File.Exists(markerPath))
        {
            return false;
        }

        string? source;
        try
        {
            var dto = JsonSerializer.Deserialize<MarkerDto>(File.ReadAllText(markerPath));
            source = dto?.Source;
        }
        catch (JsonException)
        {
            File.Delete(markerPath);
            return false;
        }

        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            File.Delete(markerPath);
            return false;
        }

        Restore(source!, databasePath);
        File.Delete(markerPath);
        return true;
    }

    /// <summary>
    /// Restaure <paramref name="sourcePath"/> dans <paramref name="destinationPath"/> via l'API
    /// Online Backup (écrase la base cible). La cible est nettoyée de ses sidecars WAL au préalable.
    /// </summary>
    internal static void Restore(string sourcePath, string destinationPath)
    {
        var destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        Delete(destinationPath);
        Delete(destinationPath + "-wal");
        Delete(destinationPath + "-shm");

        using var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly");
        source.Open();
        using var destination = new SqliteConnection($"Data Source={destinationPath}");
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class MarkerDto
    {
        public string? Source { get; set; }
    }
}
