using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using UglyToad.PdfPig;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Extrait le texte de la première page d'un PDF via PdfPig (parsing tolérant). Renvoie
/// <c>null</c> si le PDF n'a pas de couche texte (scan image) : le triage bascule alors sur
/// l'OCR. Les mots sont rejoints par des espaces pour préserver la lisibilité.
/// </summary>
public sealed class PdfTextExtractor : IDocumentTextExtractor
{
    public bool CanExtract(string extension)
        => string.Equals(extension.TrimStart('.'), "pdf", StringComparison.OrdinalIgnoreCase);

    public Task<string?> ExtractFirstPageTextAsync(FilePath path, CancellationToken cancellationToken)
        => Task.Run<string?>(() => ExtractFirstPage(path), cancellationToken);

    private static string? ExtractFirstPage(FilePath path)
    {
        using var document = PdfDocument.Open(path.Value, new ParsingOptions { UseLenientParsing = true });
        if (document.NumberOfPages < 1)
        {
            return null;
        }

        var page = document.GetPage(1);
        var words = page.GetWords().Select(w => w.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
        if (words.Length > 0)
        {
            return string.Join(' ', words);
        }

        // Repli : le texte brut de la page (peut être vide pour un scan).
        return string.IsNullOrWhiteSpace(page.Text) ? null : page.Text;
    }
}
