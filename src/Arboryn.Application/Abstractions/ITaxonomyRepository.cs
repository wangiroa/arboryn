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

    /// <summary>
    /// Taxonomie active <b>persistée</b> d'une catégorie, sans repli sur le défaut :
    /// <c>null</c> si aucune ligne active n'existe. Sert à distinguer « aucune personnalisation »
    /// de « personnalisation présente ».
    /// </summary>
    Task<CategoryTaxonomy?> GetStoredAsync(MediaCategory category, CancellationToken cancellationToken);

    /// <summary>Supprime toutes les versions persistées d'une catégorie (retour au défaut du code).</summary>
    Task DeleteAsync(MediaCategory category, CancellationToken cancellationToken);

    /// <summary>Toutes les taxonomies actives (persistées, complétées par les défauts manquants).</summary>
    Task<IReadOnlyList<CategoryTaxonomy>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Enregistre une taxonomie comme nouvelle version active pour sa catégorie (désactive
    /// la précédente). Renvoie le numéro de version créé.
    /// </summary>
    Task<int> SaveAsync(CategoryTaxonomy taxonomy, CancellationToken cancellationToken);
}
