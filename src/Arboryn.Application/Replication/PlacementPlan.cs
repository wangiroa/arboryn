using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Replication;

/// <summary>
/// Résultat du calcul de placement (Inc 10, § 5.5, étape 4) : l'ensemble des opérations pour
/// faire converger chaque volume vers son contenu cible, les conflits détectés (traités hors
/// automatisation), et l'impact espace signé par volume.
/// </summary>
public sealed record PlacementPlan(
    IReadOnlyList<PlacementOperation> Operations,
    IReadOnlyList<PlacementConflict> Conflicts,
    IReadOnlyDictionary<VolumeId, long> SpaceDeltaByVolume,
    int SkippedUnplaceable)
{
    public static PlacementPlan Empty { get; } = new(
        System.Array.Empty<PlacementOperation>(),
        System.Array.Empty<PlacementConflict>(),
        new Dictionary<VolumeId, long>(),
        0);
}

/// <summary>
/// Une opération planifiée. Pour <see cref="OperationKind.Copy"/> : de
/// <see cref="SourceVolumeId"/> vers <see cref="TargetVolumeId"/> (l'instance cible n'existe pas
/// encore, d'où <see cref="InstanceId"/> = null). Pour rename/move/delete (intra-volume) :
/// source = cible = le volume concerné, et <see cref="InstanceId"/> désigne l'instance agie.
/// </summary>
public sealed record PlacementOperation(
    OperationKind Kind,
    LogicalFileId LogicalFileId,
    FileInstanceId? InstanceId,
    VolumeId SourceVolumeId,
    VolumeId TargetVolumeId,
    string? OldRelativePath,
    string NewRelativePath,
    long Size)
{
    /// <summary>Vrai si l'opération traverse deux volumes (copie inter-support).</summary>
    public bool IsCrossVolume => SourceVolumeId != TargetVolumeId;
}

/// <summary>
/// Deux instances (ou plus) rattachées au même <c>LogicalFile</c> mais physiquement divergentes
/// (tailles distinctes) : versions différentes de la même œuvre sur des volumes différents.
/// Le calculateur n'automatise AUCUNE opération pour un tel LogicalFile — l'utilisateur tranche.
/// </summary>
public sealed record PlacementConflict(
    LogicalFileId LogicalFileId,
    string Description,
    IReadOnlyList<VolumeId> Volumes);
