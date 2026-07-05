using System.Text.Json;

namespace Arboryn.Infrastructure.Database;

/// <summary>
/// Emplacement effectif de la base de données (Inc 13, A2). Permet de placer la base
/// ailleurs que dans <c>%LOCALAPPDATA%</c> — clé USB, dossier partagé/synchronisé — pour
/// la transporter d'un PC à l'autre. La résolution est pure (testable) ; un pointeur
/// <c>db-location.json</c> stocké <b>par-machine</b> (jamais dans la base partagée, sinon
/// poule/œuf) mémorise le choix de l'utilisateur.
/// </summary>
public static class DatabaseLocation
{
    /// <summary>Variable d'environnement de plus haute priorité (chemin absolu du fichier .db).</summary>
    public const string EnvVariable = "ARBORYN_DB_PATH";

    /// <summary>Nom du pointeur par-machine, dans le dossier Arboryn de LOCALAPPDATA.</summary>
    public const string PointerFileName = "db-location.json";

    /// <summary>Nom de fichier de base par défaut.</summary>
    public const string DefaultDbFileName = "index.db";

    /// <summary>
    /// Résout le chemin absolu de la base selon la précédence (du plus fort au plus faible) :
    /// variable d'environnement → pointeur par-machine → <c>Database:FullPath</c> →
    /// <c>Database:PathRelativeToLocalAppData</c> → défaut <c>{LOCALAPPDATA}\Arboryn\index.db</c>.
    /// Pure : ne touche pas le disque (la création du dossier parent incombe à l'appelant).
    /// </summary>
    public static string Resolve(
        string localAppDataDir,
        string? envPath,
        string? pointerPath,
        string? configFullPath,
        string? configRelative)
    {
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return Path.GetFullPath(envPath);
        }

        if (!string.IsNullOrWhiteSpace(pointerPath))
        {
            return Path.GetFullPath(pointerPath);
        }

        if (!string.IsNullOrWhiteSpace(configFullPath))
        {
            return Path.GetFullPath(configFullPath);
        }

        if (!string.IsNullOrWhiteSpace(configRelative))
        {
            return Path.GetFullPath(Path.Combine(localAppDataDir, configRelative));
        }

        return Path.Combine(localAppDataDir, "Arboryn", DefaultDbFileName);
    }

    /// <summary>Lit le pointeur par-machine, ou <c>null</c> si absent/illisible.</summary>
    public static string? ReadPointer(string arborynDir)
    {
        var path = Path.Combine(arborynDir, PointerFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<PointerDto>(File.ReadAllText(path));
            return string.IsNullOrWhiteSpace(dto?.Path) ? null : dto!.Path;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Écrit (ou remplace) le pointeur par-machine vers <paramref name="databasePath"/>.</summary>
    public static void WritePointer(string arborynDir, string databasePath)
    {
        Directory.CreateDirectory(arborynDir);
        var path = Path.Combine(arborynDir, PointerFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(new PointerDto { Path = databasePath }));
    }

    /// <summary>Supprime le pointeur par-machine (retour au chemin par défaut LOCALAPPDATA).</summary>
    public static void ClearPointer(string arborynDir)
    {
        var path = Path.Combine(arborynDir, PointerFileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class PointerDto
    {
        public string? Path { get; set; }
    }
}

/// <summary>
/// Emplacement résolu de la base, exposé en DI (Inc 13). <see cref="ArborynDir"/> est le
/// dossier par-machine fixe ({LOCALAPPDATA}\Arboryn) qui héberge pointeur, logs et marqueurs ;
/// <see cref="DatabasePath"/> est le chemin effectif du fichier de base.
/// </summary>
public sealed record DatabaseLocationInfo(string ArborynDir, string DatabasePath);
