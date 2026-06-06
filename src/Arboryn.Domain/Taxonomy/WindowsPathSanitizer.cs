using System.Text;

namespace Arboryn.Domain.Taxonomy;

/// <summary>
/// Assainit les segments de chemin et noms de fichiers pour Windows/NTFS : retrait des
/// caractères interdits, des noms réservés, des points/espaces finaux, et bornage de la
/// longueur. Pure et déterministe.
/// </summary>
public static class WindowsPathSanitizer
{
    // < > : " / \ | ? * et caractères de contrôle (0..31).
    private static readonly HashSet<char> InvalidChars = new("<>:\"/\\|?*".ToCharArray());

    // Noms de périphériques réservés (insensible à la casse), avec ou sans extension.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private const int MaxSegmentLength = 200;

    /// <summary>Assainit un unique segment (dossier ou nom de fichier sans séparateur).</summary>
    public static string SanitizeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return "_";
        }

        var builder = new StringBuilder(segment.Length);
        foreach (var c in segment)
        {
            builder.Append(InvalidChars.Contains(c) || char.IsControl(c) ? ' ' : c);
        }

        // Espaces multiples → un seul ; retire les points/espaces de fin (interdits sous Windows).
        var collapsed = CollapseWhitespace(builder.ToString()).Trim().TrimEnd('.', ' ').Trim();
        if (collapsed.Length == 0)
        {
            return "_";
        }

        if (collapsed.Length > MaxSegmentLength)
        {
            collapsed = collapsed[..MaxSegmentLength].TrimEnd('.', ' ').Trim();
        }

        // Nom réservé (en considérant la partie avant la première extension) → suffixe.
        var stem = StemOf(collapsed);
        if (ReservedNames.Contains(stem))
        {
            collapsed = "_" + collapsed;
        }

        return collapsed;
    }

    /// <summary>
    /// Assainit un chemin relatif rendu par un template : découpe sur <c>/</c> et <c>\</c>,
    /// assainit chaque segment, et rejoint avec le séparateur Windows <c>\</c>.
    /// </summary>
    public static string SanitizeRelativeDirectory(string renderedPath)
    {
        if (string.IsNullOrWhiteSpace(renderedPath))
        {
            return string.Empty;
        }

        var segments = renderedPath
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizeSegment);

        return string.Join('\\', segments);
    }

    /// <summary>Assainit un nom de fichier (séparateurs traités comme caractères interdits).</summary>
    public static string SanitizeFileName(string renderedName) => SanitizeSegment(renderedName);

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;
        foreach (var c in value)
        {
            var isSpace = c == ' ';
            if (isSpace && previousWasSpace)
            {
                continue;
            }

            builder.Append(c);
            previousWasSpace = isSpace;
        }

        return builder.ToString();
    }

    private static string StemOf(string fileName)
    {
        var dot = fileName.IndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }
}
