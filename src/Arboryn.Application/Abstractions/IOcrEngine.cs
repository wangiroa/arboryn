using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Reconnaissance optique de caractères pour les documents scannés (sans couche texte).
/// L'implémentation (Tesseract) se dégrade proprement : renvoie <c>null</c> si le moteur natif
/// ou les données de langue (tessdata) sont absents, sans faire échouer le triage.
/// </summary>
public interface IOcrEngine
{
    /// <summary>Vrai si le moteur OCR est disponible (binaire natif + tessdata présents).</summary>
    bool IsAvailable { get; }

    /// <summary>Reconnaît le texte d'une image (chemin sur disque). <c>null</c> si indisponible/échec.</summary>
    Task<string?> RecognizeAsync(FilePath imagePath, CancellationToken cancellationToken);
}
