using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Accès au USN Journal NTFS pour le re-scan incrémental (Inc 9). Toujours optionnel :
/// l'implémentation se dégrade en renvoyant <c>null</c> quand le journal est indisponible
/// (volume non-NTFS, journal absent/réinitialisé, accès refusé faute d'élévation, ou
/// absence de position de référence). Le re-scan retombe alors sur un parcours mtime complet.
/// </summary>
public interface IUsnJournalReader
{
    /// <summary>
    /// Énumère les changements de fichiers survenus sous <paramref name="root"/> depuis la
    /// position <see cref="VolumeRecord.LastUsn"/>, ou <c>null</c> si le journal ne peut pas
    /// fournir un delta fiable (→ parcours complet requis).
    /// </summary>
    Task<UsnChangeSet?> TryReadChangesAsync(VolumeRecord volume, FilePath root, CancellationToken cancellationToken);

    /// <summary>
    /// Position USN courante du volume (à mémoriser comme référence après un parcours complet),
    /// ou <c>null</c> si le journal est indisponible.
    /// </summary>
    Task<long?> TryGetCurrentPositionAsync(VolumeRecord volume, CancellationToken cancellationToken);
}

/// <summary>Ensemble de changements lus dans le journal + nouvelle position à mémoriser.</summary>
public sealed record UsnChangeSet(IReadOnlyList<UsnChange> Changes, long NextUsn);

/// <summary>Un changement signalé par le journal : chemin absolu + s'il s'agit d'une suppression.</summary>
public sealed record UsnChange(FilePath Path, bool Deleted);
