using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Scanner mono-thread (Inc 1) : énumère récursivement les fichiers sous une racine.
/// Exclut de la récursion les sous-arbres « bruit » (dossiers système/cachés comme
/// AppData, points de re-parse / jonctions, dotfolders type .git, et dossiers connus
/// non-média), ainsi que les fichiers système. Les fichiers inaccessibles sont
/// journalisés et ignorés sans interrompre le scan.
///
/// La racine choisie par l'utilisateur est toujours scannée, même si son nom serait
/// exclu en tant que sous-dossier — les filtres ne s'appliquent qu'à la descente.
/// Rendre ces filtres configurables par l'utilisateur est prévu en Inc 12.
/// </summary>
public sealed class FileScanner : IFileScanner
{
    // Cède périodiquement le thread pour ne pas monopoliser l'appelant (UI).
    private const int YieldEvery = 256;

    // Dossiers exclus par défaut (bruit système / non-média).
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AppData",
        "$Recycle.Bin",
        "System Volume Information",
        "node_modules",
    };

    private static readonly EnumerationOptions ScanOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        // Les exclusions (attributs + noms) sont gérées explicitement dans les
        // prédicats ci-dessous pour un comportement déterministe.
    };

    private readonly ILogger<FileScanner> _logger;

    public FileScanner(ILogger<FileScanner> logger) => _logger = logger;

    public async IAsyncEnumerable<ScannedFile> ScanAsync(
        FilePath rootPath,
        VolumeId volumeId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var root = rootPath.Value;
        if (!Directory.Exists(root))
        {
            _logger.LogWarning("Racine de scan introuvable, scan ignoré : {Root}", root);
            yield break;
        }

        var enumeration = new FileSystemEnumerable<string>(
            root,
            static (ref FileSystemEntry entry) => entry.ToFullPath(),
            ScanOptions)
        {
            ShouldIncludePredicate = IncludeFile,
            ShouldRecursePredicate = ShouldRecurseInto,
        };

        _logger.LogInformation("Début du scan du volume {Volume} sous {Root}", volumeId, root);

        var count = 0;
        foreach (var fullPath in enumeration)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scanned = TryRead(fullPath);
            if (scanned is not null)
            {
                yield return scanned;
            }

            if (++count % YieldEvery == 0)
            {
                await Task.Yield();
            }
        }

        _logger.LogInformation("Scan terminé : {Count} fichiers retenus sous {Root}", count, root);
    }

    /// <summary>Lit les métadonnées d'un seul fichier (re-scan incrémental, Inc 9).</summary>
    public ScannedFile? TryStat(FilePath path) => TryRead(path.Value);

    /// <summary>Inclut uniquement les fichiers normaux (ni reparse-point, ni système).</summary>
    private static bool IncludeFile(ref FileSystemEntry entry) =>
        !entry.IsDirectory &&
        (entry.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) == 0;

    /// <summary>Décide si l'on descend dans un sous-dossier.</summary>
    private static bool ShouldRecurseInto(ref FileSystemEntry entry)
    {
        // Reparse-points/jonctions, dossiers système ou cachés (ex. AppData).
        if ((entry.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden)) != 0)
        {
            return false;
        }

        var name = entry.FileName;

        // Dotfolders : .git, .vscode, .nuget, .cache…
        if (name.Length > 0 && name[0] == '.')
        {
            return false;
        }

        return !ExcludedDirectoryNames.Contains(name.ToString());
    }

    /// <summary>
    /// Lit les métadonnées d'un fichier. Retourne <c>null</c> et journalise si le
    /// fichier est devenu inaccessible entre l'énumération et la lecture (verrou,
    /// suppression concurrente, permissions).
    /// </summary>
    private ScannedFile? TryRead(string fullPath)
    {
        try
        {
            var info = new FileInfo(fullPath);

            return new ScannedFile(
                FilePath.From(fullPath),
                info.Length,
                info.LastWriteTimeUtc,
                info.CreationTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Fichier ignoré (inaccessible) : {Path}", fullPath);
            return null;
        }
    }
}
