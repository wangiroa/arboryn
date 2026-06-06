using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Construit le jeu de champs exposé aux templates de taxonomie à partir des métadonnées
/// fusionnées (Inc 4) et de l'extension. Ajoute <c>ext</c> et quelques alias usuels
/// (<c>author</c>/<c>title</c>) pour les catégories où les tags portent des noms voisins.
/// </summary>
public static class TemplateFields
{
    public static IReadOnlyDictionary<string, string?> From(
        MediaCategory category, IReadOnlyDictionary<string, string> fused, string extension)
    {
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in fused)
        {
            fields[key] = value;
        }

        fields["ext"] = extension.TrimStart('.').ToLowerInvariant();

        // Alias : pour livres / livres audio, l'auteur peut venir de l'artiste d'album.
        if (IsBlank(fields, MetadataKeys.Author))
        {
            var author = FirstNonBlank(fused, MetadataKeys.AlbumArtist, MetadataKeys.Artist);
            if (author is not null)
            {
                fields[MetadataKeys.Author] = author;
            }
        }

        // Le titre de l'œuvre peut venir du tag album (livre audio mono-fichier).
        if (IsBlank(fields, MetadataKeys.Title) && fused.TryGetValue(MetadataKeys.Album, out var album) && !string.IsNullOrWhiteSpace(album))
        {
            fields[MetadataKeys.Title] = album;
        }

        return fields;
    }

    private static bool IsBlank(IReadOnlyDictionary<string, string?> fields, string key)
        => !fields.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value);

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
