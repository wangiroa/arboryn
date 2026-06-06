using Arboryn.Domain.Enums;

namespace Arboryn.Domain.Metadata;

/// <summary>
/// Associe une extension de fichier à l'ensemble des <see cref="MediaFilterType"/> auxquels elle
/// peut appartenir, pour filtrer la vue des doublons. Volontairement permissif sur les extensions
/// ambiguës : un même fichier peut ressortir sous plusieurs filtres (un PDF est document, ebook et
/// BD ; un JPG est photo et planche de BD). La distinction musique / livre audio n'est pas faite —
/// elle n'est pas fiable à partir de la seule extension (mp3, m4a… sont communs aux deux).
/// </summary>
public static class MediaFilterClassifier
{
    private static readonly IReadOnlyDictionary<MediaFilterType, string[]> ExtensionsByType =
        new Dictionary<MediaFilterType, string[]>
        {
            [MediaFilterType.Audio] = new[]
            {
                ".mp3", ".m4a", ".m4b", ".flac", ".ogg", ".opus", ".wav", ".aac",
                ".aax", ".aa", ".wma", ".alac", ".aiff", ".ape",
            },
            [MediaFilterType.Video] = new[]
            {
                ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v",
                ".mpg", ".mpeg", ".ts", ".m2ts", ".vob", ".3gp", ".divx",
            },
            [MediaFilterType.Photo] = new[]
            {
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".svg",
                ".heic", ".heif", ".raw", ".nef", ".cr2", ".cr3", ".arw", ".dng", ".rw2", ".orf", ".srw",
            },
            [MediaFilterType.Comic] = new[]
            {
                ".cbr", ".cbz", ".cb7", ".cbt", ".cba", ".pdf", ".jpg", ".jpeg", ".png",
            },
            [MediaFilterType.Document] = new[]
            {
                ".pdf", ".doc", ".docx", ".rtf", ".odt", ".xls", ".xlsx", ".ods",
                ".csv", ".txt", ".ppt", ".pptx", ".odp",
            },
            [MediaFilterType.Ebook] = new[]
            {
                ".epub", ".mobi", ".azw", ".azw3", ".kfx", ".fb2", ".pdf", ".djvu",
            },
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<MediaFilterType>> TypesByExtension = Build();

    private static readonly IReadOnlySet<MediaFilterType> Empty = new HashSet<MediaFilterType>();

    private static IReadOnlyDictionary<string, IReadOnlySet<MediaFilterType>> Build()
    {
        var map = new Dictionary<string, HashSet<MediaFilterType>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (type, extensions) in ExtensionsByType)
        {
            foreach (var extension in extensions)
            {
                if (!map.TryGetValue(extension, out var set))
                {
                    set = new HashSet<MediaFilterType>();
                    map[extension] = set;
                }

                set.Add(type);
            }
        }

        return map.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlySet<MediaFilterType>)kv.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Catégories de filtre possibles pour l'extension donnée (ensemble vide si l'extension est
    /// inconnue ou absente). L'extension peut être fournie avec ou sans point initial.
    /// </summary>
    public static IReadOnlySet<MediaFilterType> FromExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return Empty;
        }

        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        return TypesByExtension.TryGetValue(normalized, out var set) ? set : Empty;
    }
}
