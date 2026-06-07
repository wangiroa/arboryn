using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Extrait le texte de la première page d'un document (PDF natif). Renvoie <c>null</c> ou une
/// chaîne vide si le document n'a pas de couche texte (scan image) — le triage bascule alors
/// sur l'OCR via <see cref="IOcrEngine"/>.
/// </summary>
public interface IDocumentTextExtractor
{
    /// <summary>Vrai si cet extracteur sait traiter le type de fichier (par extension).</summary>
    bool CanExtract(string extension);

    Task<string?> ExtractFirstPageTextAsync(FilePath path, CancellationToken cancellationToken);
}
