using Arboryn.Domain.Enums;
using Arboryn.Domain.Taxonomy;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Accès à la taxonomie canonique (table <c>library_taxonomy</c>). Renvoie la taxonomie
/// active persistée d'une catégorie, ou à défaut celle livrée par
/// <see cref="DefaultTaxonomies"/>. La personnalisation (UI Settings) est versionnée.
/// </summary>
public interface ITaxonomyRepository
{
    /// <summary>Taxonomie active d'une catégorie (persistée ou défaut), ou <c>null</c> si non uniformisable.</summary>
    Task<CategoryTaxonomy?> GetAsync(MediaCategory category, CancellationToken cancellationToken);

    /// <summary>Toutes les taxonomies actives (persistées, complétées par les défauts manquants).</summary>
    Task<IReadOnlyList<CategoryTaxonomy>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Enregistre une taxonomie comme nouvelle version active pour sa catégorie (désactive
    /// la précédente). Renvoie le numéro de version créé.
    /// </summary>
    Task<int> SaveAsync(CategoryTaxonomy taxonomy, CancellationToken cancellationToken);
}
