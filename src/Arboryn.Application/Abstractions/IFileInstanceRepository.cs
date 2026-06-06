using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

public interface IFileInstanceRepository
{
    /// <summary>
    /// Insère ou met à jour une FileInstance et renvoie l'id réel en base. Sur conflit
    /// <c>(volume_id, relative_path)</c>, c'est l'id de la ligne existante qui est conservé.
    /// </summary>
    Task<FileInstanceId> UpsertAsync(FileInstanceRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<FileInstanceRecord>> GetDuplicateCandidatesAsync(VolumeId volumeId, CancellationToken cancellationToken);

    /// <summary>
    /// Candidats doublons, optionnellement limités aux fichiers situés sous
    /// <paramref name="underRoot"/> (chemin absolu). <c>null</c> = tout le volume.
    /// </summary>
    Task<IReadOnlyList<FileInstanceRecord>> GetDuplicateCandidatesAsync(VolumeId volumeId, FilePath? underRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Toutes les instances actives d'un volume (optionnellement sous <paramref name="underRoot"/>),
    /// utilisé par la détection floue qui compare les noms deux à deux.
    /// </summary>
    Task<IReadOnlyList<FileInstanceRecord>> GetActiveInstancesAsync(VolumeId volumeId, FilePath? underRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Met à jour le chemin (et le nom canonique dérivé) d'une instance après un
    /// déplacement/renommage d'uniformisation. En Inc 1-8, <paramref name="newPath"/> est absolu.
    /// </summary>
    Task UpdatePathAsync(FileInstanceId id, FilePath newPath, CancellationToken cancellationToken);

    /// <summary>Supprime toutes les FileInstances d'un volume (reset du catalogue).</summary>
    Task ClearVolumeAsync(VolumeId volumeId, CancellationToken cancellationToken);

    /// <summary>Marque une instance comme supprimée (exclue de la détection).</summary>
    Task MarkDeletedAsync(FileInstanceId id, CancellationToken cancellationToken);

    /// <summary>Réactive une instance précédemment supprimée (undo).</summary>
    Task MarkActiveAsync(FileInstanceId id, CancellationToken cancellationToken);
}

/// <summary>
/// Instance physique d'un fichier sur un volume. En Inc 1 (volume unique « default »),
/// <see cref="Path"/> est le chemin absolu ; le modèle volume + chemin relatif arrivera
/// en Inc 9. <see cref="LogicalFileId"/> rattache l'instance à son LogicalFile (Inc 3).
/// </summary>
public sealed record FileInstanceRecord(
    FileInstanceId Id,
    VolumeId VolumeId,
    FilePath Path,
    CanonicalName CanonicalName,
    long Size,
    DateTime ModifiedAt)
{
    /// <summary>Optionnel : LogicalFile auquel rattacher cette instance (nullable Inc 3).</summary>
    public LogicalFileId? LogicalFileId { get; init; }
}
