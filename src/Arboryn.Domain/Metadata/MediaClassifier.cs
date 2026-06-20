using Arboryn.Domain.Enums;

namespace Arboryn.Domain.Metadata;

/// <summary>
/// Classification préliminaire d'un fichier selon son extension. La catégorie peut
/// être affinée plus tard par lecture des métadonnées de contenu (EXIF/ID3/PDF Info)
/// et par les règles utilisateur (Inc 7 — triage documents).
/// </summary>
public static class MediaClassifier
{
    private static readonly HashSet<string> AudiobookExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4a", ".m4b", ".flac", ".ogg", ".opus", ".wav", ".aac", ".aax",
    };

    private static readonly HashSet<string> BookExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".epub", ".mobi", ".azw", ".azw3", ".kfx", ".fb2",
    };

    // Comics et BD : archives d'images paginées. Une même série est souvent découpée en
    // plusieurs fichiers (un par tome/numéro) → traitée comme œuvre multi-fichiers.
    private static readonly HashSet<string> ComicExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cbz", ".cbr", ".cb7", ".cbt", ".cba",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v", ".mpg", ".mpeg", ".ts",
    };

    private static readonly HashSet<string> PhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp",
        ".heic", ".heif", ".raw", ".nef", ".cr2", ".cr3", ".arw", ".dng", ".rw2", ".orf", ".srw",
    };

    private static readonly HashSet<string> OtherDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".rtf", ".odt", ".xls", ".xlsx", ".ods", ".csv",
        ".txt", ".ppt", ".pptx", ".odp",
    };

    public static MediaCategory FromExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return MediaCategory.Unknown;
        }

        var normalized = extension.StartsWith('.') ? extension : "." + extension;

        if (PhotoExtensions.Contains(normalized))
        {
            return MediaCategory.Photo;
        }

        if (VideoExtensions.Contains(normalized))
        {
            return MediaCategory.Video;
        }

        if (AudiobookExtensions.Contains(normalized))
        {
            return MediaCategory.Audiobook;
        }

        if (BookExtensions.Contains(normalized))
        {
            return MediaCategory.Book;
        }

        if (ComicExtensions.Contains(normalized))
        {
            return MediaCategory.Comic;
        }

        if (OtherDocumentExtensions.Contains(normalized))
        {
            return MediaCategory.OtherDocument;
        }

        return MediaCategory.Unknown;
    }
}
