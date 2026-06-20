using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enrichment;
using Arboryn.Domain.Enums;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Construit une <see cref="EnrichmentQuery"/> à partir des métadonnées fusionnées d'un fichier.
/// C'est ICI qu'est ancrée la garantie <em>privacy-first</em> : seules des clés explicitement
/// autorisées par catégorie (titre, auteur, ISBN, année…) sont recopiées dans la requête.
/// Le nom de fichier et le chemin ne figurent jamais dans la liste blanche, donc ne peuvent
/// pas fuir vers un provider. Toute autre clé présente dans les métadonnées est ignorée.
/// </summary>
public static class EnrichmentQueryBuilder
{
    // Liste blanche des champs transmissibles par catégorie. Tout le reste est exclu.
    private static readonly IReadOnlyDictionary<MediaCategory, string[]> Allowed =
        new Dictionary<MediaCategory, string[]>
        {
            [MediaCategory.Book] = new[]
            {
                MetadataKeys.Isbn, MetadataKeys.Title, MetadataKeys.Author,
                MetadataKeys.Year, MetadataKeys.Publisher, MetadataKeys.Language,
            },
            [MediaCategory.Comic] = new[]
            {
                MetadataKeys.Isbn, MetadataKeys.Title, MetadataKeys.Author, MetadataKeys.Year,
            },
            [MediaCategory.Audiobook] = new[]
            {
                MetadataKeys.Title, MetadataKeys.Author, MetadataKeys.Year,
            },
            [MediaCategory.Video] = new[]
            {
                MetadataKeys.Title, MetadataKeys.Year,
            },
        };

    /// <summary>
    /// Produit la requête d'enrichissement, ou une requête vide (<see cref="EnrichmentQuery.IsEmpty"/>)
    /// si la catégorie n'est pas enrichissable ou qu'aucun champ exploitable n'est disponible.
    /// </summary>
    public static EnrichmentQuery Build(MediaCategory category, IReadOnlyDictionary<string, string> fused)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Allowed.TryGetValue(category, out var keys))
        {
            return new EnrichmentQuery(category, fields);
        }

        foreach (var key in keys)
        {
            var value = Resolve(key, fused);
            if (!string.IsNullOrWhiteSpace(value))
            {
                fields[key] = value!.Trim();
            }
        }

        return new EnrichmentQuery(category, fields);
    }

    /// <summary>
    /// Résout un champ avec les alias usuels : l'auteur peut venir de <c>album_artist</c>/<c>artist</c>,
    /// le titre du tag <c>album</c> (livre audio mono-fichier). N'élargit jamais la liste blanche.
    /// </summary>
    private static string? Resolve(string key, IReadOnlyDictionary<string, string> fused)
    {
        if (fused.TryGetValue(key, out var direct) && !string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        return key switch
        {
            MetadataKeys.Author => FirstNonBlank(fused, MetadataKeys.AlbumArtist, MetadataKeys.Artist),
            MetadataKeys.Title => FirstNonBlank(fused, MetadataKeys.Album),
            _ => null,
        };
    }

    private static string? FirstNonBlank(IReadOnlyDictionary<string, string> fused, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (fused.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
