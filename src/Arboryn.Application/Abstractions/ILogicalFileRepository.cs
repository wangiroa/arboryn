using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Dépôt des <see cref="LogicalFile"/> : œuvres/documents identifiés par leur
/// signature de contenu. Toute <see cref="FileInstanceRecord"/> doit être rattachée
/// à un LogicalFile via sa signature.
/// </summary>
public interface ILogicalFileRepository
{
    Task<LogicalFile?> FindBySignatureAsync(ContentSignature signature, CancellationToken cancellationToken);

    Task UpsertAsync(LogicalFile logicalFile, CancellationToken cancellationToken);

    /// <summary>Met à jour la catégorie d'un LogicalFile (affinement post-extraction).</summary>
    Task UpdateCategoryAsync(LogicalFileId id, MediaCategory category, CancellationToken cancellationToken);

    /// <summary>
    /// Met à jour la catégorie du LogicalFile rattaché à une FileInstance (affinement par triage).
    /// Sans effet si l'instance n'est rattachée à aucun LogicalFile. Renvoie le nombre de
    /// LogicalFiles modifiés (0 ou 1).
    /// </summary>
    Task<int> SetCategoryByInstanceAsync(FileInstanceId instanceId, MediaCategory category, CancellationToken cancellationToken);

    /// <summary>
    /// Rattache toutes les FileInstances orphelines (<c>logical_file_id IS NULL</c>) :
    /// pour chaque signature <c>name_size</c> manquante, crée le LogicalFile correspondant
    /// puis met à jour les instances. Idempotent — sans effet si tout est déjà rattaché.
    /// </summary>
    Task BackfillUnattachedAsync(CancellationToken cancellationToken);

    /// <summary>Supprime les LogicalFiles qui ne sont plus référencés par aucune FileInstance.</summary>
    Task DeleteOrphansAsync(CancellationToken cancellationToken);

    /// <summary>Métriques globales du catalogue pour un volume.</summary>
    Task<CatalogMetrics> GetMetricsAsync(VolumeId volumeId, CancellationToken cancellationToken);

    /// <summary>Résumés par LogicalFile pour la vue inventaire, triés par espace récupérable.</summary>
    Task<IReadOnlyList<LogicalFileSummary>> GetSummariesAsync(VolumeId volumeId, CancellationToken cancellationToken);
}

/// <summary>Statistiques du catalogue (par volume).</summary>
public sealed record CatalogMetrics(long LogicalFiles, long FileInstances)
{
    /// <summary>Ratio de redondance : instances par LogicalFile (1.0 = pas de doublon).</summary>
    public double RedundancyRatio => LogicalFiles == 0 ? 0 : (double)FileInstances / LogicalFiles;
}

/// <summary>Résumé d'un LogicalFile et de ses instances physiques sur un volume.</summary>
public sealed record LogicalFileSummary(
    LogicalFileId Id,
    ContentSignature Signature,
    int InstanceCount,
    long TotalSize,
    long MaxSize,
    FileInstanceId SampleInstanceId)
{
    /// <summary>Espace récupérable si l'on ne garde qu'une copie : total − plus grande.</summary>
    public long ReclaimableBytes => TotalSize - MaxSize;
}
