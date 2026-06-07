namespace Arboryn.Domain.Triage;

/// <summary>
/// Détermine quels fichiers relèvent du workflow de triage des documents officiels : PDF et
/// images scannées. Les autres bureautiques (.docx, .xlsx…) sont hors périmètre du triage
/// (pas de couche texte première-page exploitable de la même façon).
/// </summary>
public static class TriageCandidates
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".webp", ".heic",
    };

    public static bool IsCandidate(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        return Extensions.Contains(normalized);
    }
}
