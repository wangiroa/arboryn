using Arboryn.Application.Abstractions;
using Arboryn.Domain.Triage;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Prépare le triage d'un dossier : repère les documents candidats (PDF + scans), en extrait
/// le texte de la première page (couche texte native ; OCR en repli pour les scans), rend une
/// miniature, et pré-remplit les trois champs (source / objet / date) via les patterns de
/// triage. Ne modifie rien sur disque hormis la création des miniatures.
/// </summary>
public sealed class PrepareTriageHandler
{
    private const int SnippetLength = 400;
    private const int ThumbnailWidth = 220;

    private readonly IFileInstanceRepository _instances;
    private readonly ITriageRepository _triage;
    private readonly IEnumerable<IDocumentTextExtractor> _textExtractors;
    private readonly IOcrEngine _ocr;
    private readonly IDocumentThumbnailRenderer _thumbnails;
    private readonly ILogger<PrepareTriageHandler> _logger;

    public PrepareTriageHandler(
        IFileInstanceRepository instances,
        ITriageRepository triage,
        IEnumerable<IDocumentTextExtractor> textExtractors,
        IOcrEngine ocr,
        IDocumentThumbnailRenderer thumbnails,
        ILogger<PrepareTriageHandler> logger)
    {
        _instances = instances;
        _triage = triage;
        _textExtractors = textExtractors;
        _ocr = ocr;
        _thumbnails = thumbnails;
        _logger = logger;
    }

    public async Task<TriagePreparation> ExecuteAsync(
        VolumeId volumeId, FilePath libraryRoot, string thumbnailDirectory, CancellationToken cancellationToken = default)
    {
        await _triage.EnsureDefaultPatternsAsync(DefaultTriagePatterns.All, cancellationToken).ConfigureAwait(false);
        var patterns = await _triage.GetActivePatternsAsync(cancellationToken).ConfigureAwait(false);

        var instances = await _instances
            .GetActiveInstancesAsync(volumeId, libraryRoot, cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(thumbnailDirectory);

        var candidates = new List<TriageCandidate>();
        var ocrUsed = 0;

        foreach (var instance in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TriageCandidates.IsCandidate(instance.Path.Extension))
            {
                continue;
            }

            var thumbnail = await RenderThumbnailAsync(instance.Path, thumbnailDirectory, cancellationToken)
                .ConfigureAwait(false);

            var text = await ExtractTextAsync(instance.Path, cancellationToken).ConfigureAwait(false);

            // Pas de couche texte (scan) → OCR sur la miniature rendue, si disponible.
            if (string.IsNullOrWhiteSpace(text) && thumbnail is { } thumb && _ocr.IsAvailable)
            {
                text = await SafeOcrAsync(thumb, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    ocrUsed++;
                }
            }

            var extraction = TriageExtractor.Extract(text, patterns);
            candidates.Add(new TriageCandidate(
                instance.Id, instance.Path, thumbnail, BuildSnippet(text), extraction));
        }

        _logger.LogInformation(
            "Triage préparé : {Count} document(s) candidat(s), {Ocr} via OCR.", candidates.Count, ocrUsed);

        return new TriagePreparation(candidates, ocrUsed, _ocr.IsAvailable);
    }

    private async Task<string?> ExtractTextAsync(FilePath path, CancellationToken cancellationToken)
    {
        var extractor = _textExtractors.FirstOrDefault(e => e.CanExtract(path.Extension));
        if (extractor is null)
        {
            return null;
        }

        try
        {
            return await extractor.ExtractFirstPageTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Extraction de texte impossible pour {Path}", path.Value);
            return null;
        }
    }

    private async Task<FilePath?> RenderThumbnailAsync(
        FilePath path, string thumbnailDirectory, CancellationToken cancellationToken)
    {
        if (!_thumbnails.CanRender(path.Extension))
        {
            return null;
        }

        try
        {
            return await _thumbnails
                .RenderFirstPageAsync(path, thumbnailDirectory, ThumbnailWidth, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rendu de miniature impossible pour {Path}", path.Value);
            return null;
        }
    }

    private async Task<string?> SafeOcrAsync(FilePath imagePath, CancellationToken cancellationToken)
    {
        try
        {
            return await _ocr.RecognizeAsync(imagePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OCR impossible pour {Path}", imagePath.Value);
            return null;
        }
    }

    private static string BuildSnippet(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= SnippetLength ? collapsed : collapsed[..SnippetLength] + "…";
    }
}

/// <summary>Un document prêt à trier : miniature, extrait texte, et champs pré-remplis.</summary>
public sealed record TriageCandidate(
    FileInstanceId InstanceId,
    FilePath Path,
    FilePath? ThumbnailPath,
    string Snippet,
    TriageExtraction Extraction);

/// <summary>Résultat de la préparation du triage d'un dossier.</summary>
public sealed record TriagePreparation(
    IReadOnlyList<TriageCandidate> Candidates, int OcrUsed, bool OcrAvailable);
