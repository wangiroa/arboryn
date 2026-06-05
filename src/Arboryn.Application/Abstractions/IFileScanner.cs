using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Énumère, de façon paresseuse et asynchrone, les fichiers d'un volume sous une
/// racine donnée. L'implémentation est mono-thread (Inc 1) ; la progression est
/// dérivée par le consommateur à partir du flux de <see cref="ScannedFile"/>.
/// </summary>
public interface IFileScanner
{
    IAsyncEnumerable<ScannedFile> ScanAsync(
        FilePath rootPath,
        VolumeId volumeId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Fichier rencontré durant un scan. <see cref="Path"/> est le chemin absolu, requis
/// pour les opérations fichier ultérieures (suppression). <see cref="CreatedAt"/> est
/// nullable car la date de création n'est pas fiable sur tous les systèmes de fichiers.
/// </summary>
public sealed record ScannedFile(
    FilePath Path,
    long Size,
    DateTime ModifiedAt,
    DateTime? CreatedAt);
