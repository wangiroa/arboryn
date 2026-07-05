using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Lecture brute du catalogue logique pour la réplication (Inc 10) : chaque <c>LogicalFile</c>
/// et l'ensemble de ses instances actives, réparties sur les volumes. L'enrichissement (chemin
/// canonique, sous-catégorie, année) est réalisé en aval par l'assembleur.
/// </summary>
public interface IReplicationCatalogReader
{
    Task<IReadOnlyList<LogicalFileInstances>> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Un <c>LogicalFile</c> et ses instances physiques actives (tous volumes confondus).</summary>
public sealed record LogicalFileInstances(
    LogicalFileId Id,
    MediaCategory Category,
    IReadOnlyList<CatalogInstanceRow> Instances);

/// <summary>Ligne d'instance active du catalogue (chemin relatif à la racine du volume).</summary>
public sealed record CatalogInstanceRow(
    FileInstanceId Id,
    VolumeId VolumeId,
    string RelativePath,
    long Size);
