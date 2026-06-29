using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Dépôt des volumes (Inc 9). La ligne « default » (<see cref="VolumeId.Default"/>)
/// existe depuis l'Inc 3 ; l'enrôlement de volumes réels et leur identification
/// stable arrivent ici.
/// </summary>
public interface IVolumeRepository
{
    /// <summary>Volume par son id, ou <c>null</c>.</summary>
    Task<VolumeRecord?> GetAsync(VolumeId id, CancellationToken cancellationToken);

    /// <summary>Volume dont le numéro de série (VSN NTFS) correspond, ou <c>null</c>.</summary>
    Task<VolumeRecord?> FindBySerialAsync(string serial, CancellationToken cancellationToken);

    /// <summary>Volume dont l'empreinte (hostname+partage SMB) correspond, ou <c>null</c>.</summary>
    Task<VolumeRecord?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken);

    /// <summary>Tous les volumes, « default » inclus, triés par nom.</summary>
    Task<IReadOnlyList<VolumeRecord>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Insère ou met à jour un volume (clé : <see cref="VolumeRecord.Id"/>).</summary>
    Task UpsertAsync(VolumeRecord record, CancellationToken cancellationToken);

    /// <summary>Met à jour le statut de connexion d'un volume.</summary>
    Task SetStatusAsync(VolumeId id, VolumeStatus status, CancellationToken cancellationToken);

    /// <summary>
    /// Enregistre la fin d'un scan : horodatage et, sur NTFS, position USN Journal
    /// (<paramref name="lastUsn"/> = <c>null</c> si non applicable).
    /// </summary>
    Task RecordScanAsync(VolumeId id, DateTime scannedAtUtc, long? lastUsn, CancellationToken cancellationToken);
}

/// <summary>
/// État persistant d'un volume. Les champs d'identification (<see cref="Serial"/>,
/// <see cref="Fingerprint"/>) permettent de reconnaître un support rebranché même
/// si sa lettre de lecteur a changé. <see cref="MountPoint"/> est le chemin d'accès
/// courant (volatile entre branchements).
/// </summary>
public sealed record VolumeRecord(
    VolumeId Id,
    string Name,
    VolumeKind Kind,
    VolumeStatus Status)
{
    /// <summary>Numéro de série du volume (VSN NTFS, hex), ou <c>null</c>.</summary>
    public string? Serial { get; init; }

    /// <summary>Empreinte stable pour SMB : <c>\\hôte\partage</c> normalisé, ou <c>null</c>.</summary>
    public string? Fingerprint { get; init; }

    /// <summary>Étiquette de volume (label du système de fichiers).</summary>
    public string? Label { get; init; }

    /// <summary>Chemin de montage courant (par ex. <c>E:\</c>) ; peut changer entre branchements.</summary>
    public string? MountPoint { get; init; }

    /// <summary>Dernière position USN Journal connue (NTFS), pour le re-scan incrémental.</summary>
    public long? LastUsn { get; init; }

    public DateTime? LastSeenAt { get; init; }

    public DateTime? LastScanAt { get; init; }

    /// <summary>Scope de réplication associé (Inc 10), ou <c>null</c>.</summary>
    public string? ReplicationScopeId { get; init; }
}
