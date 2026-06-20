using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Construit le jeu de champs exposé aux templates de taxonomie à partir des métadonnées
/// fusionnées (Inc 4) et de l'extension. Ajoute <c>ext</c> et quelques alias usuels
/// (<c>author</c>/<c>title</c>) pour les catégories où les tags portent des noms voisins.
///
/// Gère aussi les <b>œuvres multi-fichiers</b> (livres audio / comics découpés en pistes) :
/// quand le fichier ne porte pas de titre d'œuvre propre — titre absent ou réduit à un
/// marqueur de position (« 001 chapitre 1 ») — le titre de l'œuvre est tiré du nom du
/// dossier parent (auteur retiré) et la position du fichier devient un libellé de
/// <c>chapter</c>. Le dossier parent désigne alors l'œuvre, pas le nom de fichier.
/// </summary>
public static class TemplateFields
{
    public static IReadOnlyDictionary<string, string?> From(
        MediaCategory category,
        IReadOnlyDictionary<string, string> fused,
        string extension,
        string? fileStem = null,
        string? parentDirectoryName = null,
        string? chapterNumber = null)
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

        ApplyDirectoryWorkTitle(category, fields, fileStem, parentDirectoryName, chapterNumber);

        return fields;
    }

    /// <summary>
    /// Pour les œuvres multi-fichiers : si le fichier n'est qu'une partie (titre inexploitable
    /// comme titre d'œuvre), remplace le titre par le nom du dossier parent débarrassé de
    /// l'auteur connu, et expose le numéro de séquence (déjà calculé au niveau de l'œuvre et
    /// zero-paddé par le planificateur) comme <c>chapter</c>.
    /// </summary>
    private static void ApplyDirectoryWorkTitle(
        MediaCategory category,
        Dictionary<string, string?> fields,
        string? fileStem,
        string? parentDirectoryName,
        string? chapterNumber)
    {
        if (string.IsNullOrWhiteSpace(parentDirectoryName))
        {
            return;
        }

        if (!MultiFileWork.IsPartFile(category, fileStem))
        {
            return;
        }

        fields.TryGetValue(MetadataKeys.Author, out var author);
        var workTitle = MultiFileWork.WorkTitle(parentDirectoryName!, author);
        if (string.IsNullOrWhiteSpace(workTitle))
        {
            return;
        }

        fields[MetadataKeys.Title] = workTitle;

        if (!string.IsNullOrWhiteSpace(chapterNumber))
        {
            fields[MetadataKeys.Chapter] = chapterNumber;
        }
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
