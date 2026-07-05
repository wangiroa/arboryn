using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Lecture agrégée du catalogue pour le tableau de bord inventaire (Inc 11). Requêtes SQL
/// indexées (rapides à 50k+ œuvres) : matrice de présence volume × catégorie, totaux par
/// catégorie, compteurs globaux, et recherche cross-volume « où est X ? ».
/// </summary>
public interface IInventoryReader
{
    /// <summary>Présence effective : par (volume, catégorie), nombre d'instances actives et espace physique.</summary>
    Task<IReadOnlyList<VolumeCategoryPresence>> GetPresenceAsync(CancellationToken cancellationToken);

    /// <summary>Univers par catégorie : nombre d'œuvres distinctes et espace total.</summary>
    Task<IReadOnlyList<CategoryTotal>> GetCategoryTotalsAsync(CancellationToken cancellationToken);

    /// <summary>Compteurs globaux du catalogue.</summary>
    Task<GlobalInventoryCounts> GetGlobalCountsAsync(CancellationToken cancellationToken);

    /// <summary>Recherche cross-volume : instances actives dont le nom/chemin contient <paramref name="query"/>.</summary>
    Task<IReadOnlyList<CrossVolumeSearchHit>> SearchAsync(string query, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Manques (Inc 11) : œuvres des catégories <paramref name="inScopeCategories"/> présentes
    /// quelque part mais ABSENTES du volume — donc à copier vers lui.
    /// </summary>
    Task<IReadOnlyList<InventoryWorkItem>> GetMissingAsync(
        VolumeId volumeId, IReadOnlyList<MediaCategory> inScopeCategories, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Surplus (Inc 11) : instances actives présentes sur le volume dont la catégorie n'est PAS
    /// dans son périmètre — donc à supprimer ou déplacer.
    /// </summary>
    Task<IReadOnlyList<InventoryWorkItem>> GetSurplusAsync(
        VolumeId volumeId, IReadOnlyList<MediaCategory> inScopeCategories, int limit, CancellationToken cancellationToken);
}

/// <summary>Une œuvre listée dans un détail gap/surplus : catégorie + nom d'affichage.</summary>
public sealed record InventoryWorkItem(MediaCategory Category, string Name);

/// <summary>Présence par (volume, catégorie) : nombre d'instances actives et espace physique (octets).</summary>
public sealed record VolumeCategoryPresence(VolumeId VolumeId, MediaCategory Category, int Count, long SpaceBytes);

/// <summary>Univers d'une catégorie : œuvres distinctes et espace physique cumulé.</summary>
public sealed record CategoryTotal(MediaCategory Category, int LogicalFiles, long SpaceBytes);

/// <summary>Compteurs globaux du catalogue.</summary>
public sealed record GlobalInventoryCounts(long LogicalFiles, long FileInstances, long TotalSpaceBytes)
{
    /// <summary>Ratio de redondance : instances par œuvre (1.0 = aucune copie superflue).</summary>
    public double RedundancyRatio => LogicalFiles == 0 ? 0 : (double)FileInstances / LogicalFiles;
}

/// <summary>Une instance trouvée par la recherche cross-volume.</summary>
public sealed record CrossVolumeSearchHit(
    LogicalFileId LogicalFileId, MediaCategory Category, string RelativePath, string VolumeName);
