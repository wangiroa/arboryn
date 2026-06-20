using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Metadata;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Enrichissement à la demande sur un ou plusieurs répertoires : énumère les FileInstances
/// actives sous chaque racine et applique <see cref="EnrichMetadataHandler"/> à chacune, en
/// déduisant la catégorie du LogicalFile affiné (sinon de l'extension). Agrège l'impact pour
/// l'UI. L'autorisation réseau et le seuil de confiance sont gérés par le handler unitaire.
/// </summary>
public sealed class EnrichDirectoryHandler
{
    private readonly IFileInstanceRepository _instances;
    private readonly EnrichMetadataHandler _enrich;
    private readonly ILogger<EnrichDirectoryHandler> _logger;

    public EnrichDirectoryHandler(
        IFileInstanceRepository instances,
        EnrichMetadataHandler enrich,
        ILogger<EnrichDirectoryHandler> logger)
    {
        _instances = instances;
        _enrich = enrich;
        _logger = logger;
    }

    public Task<EnrichDirectoryResult> ExecuteAsync(
        VolumeId volumeId, FilePath root, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(volumeId, new[] { root }, progress, cancellationToken);

    /// <summary>Enrichit tout le catalogue d'un volume (sans restriction de répertoire).</summary>
    public Task<EnrichDirectoryResult> ExecuteCatalogAsync(
        VolumeId volumeId, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(volumeId, new FilePath?[] { null }, progress, cancellationToken);

    public Task<EnrichDirectoryResult> ExecuteAsync(
        VolumeId volumeId, IReadOnlyList<FilePath> roots, IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(volumeId, roots.Select(r => (FilePath?)r).ToList(), progress, cancellationToken);

    private async Task<EnrichDirectoryResult> ExecuteAsync(
        VolumeId volumeId, IReadOnlyList<FilePath?> roots, IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processed = 0;
        var enrichedFiles = 0;
        var appliedFields = 0;
        var candidates = new List<EnrichmentCandidate>();

        foreach (var root in roots)
        {
            var instances = await _instances
                .GetActiveInstancesAsync(volumeId, root, cancellationToken).ConfigureAwait(false);

            foreach (var instance in instances)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Dédoublonne au cas où des racines se chevauchent.
                if (!seen.Add(instance.Path.Value))
                {
                    continue;
                }

                var category = instance.Category is { } refined && refined != MediaCategory.Unknown
                    ? refined
                    : MediaClassifier.FromExtension(instance.Path.Extension);

                var outcome = await _enrich.ExecuteAsync(instance.Id, category, cancellationToken).ConfigureAwait(false);
                processed++;
                progress?.Report(processed);

                if (outcome.AppliedFields > 0 || outcome.Candidates.Count > 0)
                {
                    enrichedFiles++;
                    appliedFields += outcome.AppliedFields;
                    candidates.AddRange(outcome.Candidates);
                }
            }
        }

        _logger.LogInformation(
            "Enrichissement de {Roots} racine(s) : {Processed} fichier(s) traité(s), {Enriched} enrichi(s), {Applied} champ(s) appliqué(s).",
            roots.Count, processed, enrichedFiles, appliedFields);

        return new EnrichDirectoryResult(processed, enrichedFiles, appliedFields, candidates);
    }
}

/// <summary>Bilan d'un enrichissement de répertoire(s).</summary>
public sealed record EnrichDirectoryResult(
    int Processed, int EnrichedFiles, int AppliedFields, IReadOnlyList<EnrichmentCandidate> Candidates);
