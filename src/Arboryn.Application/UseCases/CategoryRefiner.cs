using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Affinement de la catégorie préliminaire (déduite de l'extension) à partir des
/// métadonnées extraites du contenu — étape 4 du pipeline de catégorisation (§5.4).
///
/// Pure et déterministe : ne traite que les cas où le signal métadonnée est fiable.
/// Quand rien ne s'applique, la catégorie préliminaire est conservée telle quelle.
/// </summary>
public static class CategoryRefiner
{
    /// <summary>
    /// Renvoie la catégorie affinée d'après les métadonnées fusionnées
    /// (clé → valeur, conventions <see cref="MetadataKeys"/>).
    /// </summary>
    public static MediaCategory Refine(
        MediaCategory preliminary, IReadOnlyDictionary<string, string> metadata)
    {
        // Un document générique (.pdf, .doc…) porteur d'un ISBN est un livre (ebook).
        if (preliminary == MediaCategory.OtherDocument && HasValue(metadata, MetadataKeys.Isbn))
        {
            return MediaCategory.Book;
        }

        return preliminary;
    }

    private static bool HasValue(IReadOnlyDictionary<string, string> metadata, string key)
        => metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
}
