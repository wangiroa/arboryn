using System.Globalization;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Metadata;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Pipeline d'extraction des métadonnées d'un fichier indexé (Inc 4) :
/// classification → cleanup filename → lecture des métadonnées du contenu (selon la
/// catégorie, via les <see cref="IContentMetadataReader"/> enregistrés)
/// → persistance dans <c>file_metadata</c> avec confiance par source.
/// </summary>
public sealed class ExtractMetadataHandler
{
    // Confiance par source — la fusion privilégie les sources de plus haute confiance.
    private const double ConfidenceFilename = 0.5;
    private const double ConfidenceFilenameYear = 0.6;

    private readonly IFileMetadataRepository _metadata;
    private readonly IReadOnlyList<IContentMetadataReader> _contentReaders;
    private readonly ILogger<ExtractMetadataHandler> _logger;

    public ExtractMetadataHandler(
        IFileMetadataRepository metadata,
        IEnumerable<IContentMetadataReader> contentReaders,
        ILogger<ExtractMetadataHandler> logger)
    {
        _metadata = metadata;
        _contentReaders = contentReaders.ToList();
        _logger = logger;
    }

    /// <summary>
    /// Extrait et persiste les métadonnées de l'instance, puis renvoie la catégorie
    /// affinée d'après le contenu (§5.4 étape 4). La catégorie préliminaire est déduite
    /// de l'extension ; elle peut être promue si les métadonnées le justifient.
    /// </summary>
    public async Task<MediaCategory> ExecuteAsync(
        FileInstanceId instanceId,
        FilePath path,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var filename = path.FileName;
        var category = MediaClassifier.FromExtension(path.Extension);

        // 1) Nettoyage du nom de fichier — toujours appliqué, confiance modeste.
        await PersistFilenameMetadataAsync(instanceId, filename, now, cancellationToken).ConfigureAwait(false);

        // 2) Lecture des métadonnées du contenu : tous les lecteurs qui savent traiter
        //    cette catégorie (audio, PDF, EPUB, image…). Les échecs sont isolés par fichier.
        foreach (var reader in _contentReaders)
        {
            if (reader.CanRead(category))
            {
                await ExtractContentAsync(instanceId, path, reader, now, cancellationToken).ConfigureAwait(false);
            }
        }

        // 3) Affinement de la catégorie d'après les métadonnées fusionnées.
        var fused = await _metadata.GetFusedAsync(instanceId, cancellationToken).ConfigureAwait(false);
        var refined = CategoryRefiner.Refine(category, fused.ToDictionary(
            kvp => kvp.Key, kvp => kvp.Value.Value ?? string.Empty));
        return refined;
    }

    private async Task PersistFilenameMetadataAsync(
        FileInstanceId instanceId, string filename, DateTime now, CancellationToken cancellationToken)
    {
        var parsed = FilenameCleaner.Extract(filename);

        await UpsertIfPresentAsync(instanceId, MetadataKeys.Title, parsed.CleanTitle,
            MetadataSources.Filename, ConfidenceFilename, now, cancellationToken).ConfigureAwait(false);

        if (parsed.Year is { } year)
        {
            await _metadata.UpsertAsync(new MetadataEntry(
                instanceId, MetadataKeys.Year,
                year.ToString(CultureInfo.InvariantCulture),
                MetadataSources.Filename, ConfidenceFilenameYear, now), cancellationToken).ConfigureAwait(false);
        }

        await UpsertIfPresentAsync(instanceId, MetadataKeys.Resolution, parsed.Resolution,
            MetadataSources.Filename, ConfidenceFilename, now, cancellationToken).ConfigureAwait(false);
        await UpsertIfPresentAsync(instanceId, MetadataKeys.Source, parsed.Source,
            MetadataSources.Filename, ConfidenceFilename, now, cancellationToken).ConfigureAwait(false);
        await UpsertIfPresentAsync(instanceId, MetadataKeys.Codec, parsed.Codec,
            MetadataSources.Filename, ConfidenceFilename, now, cancellationToken).ConfigureAwait(false);
        await UpsertIfPresentAsync(instanceId, MetadataKeys.Language, parsed.Language,
            MetadataSources.Filename, ConfidenceFilename, now, cancellationToken).ConfigureAwait(false);
        await UpsertIfPresentAsync(instanceId, MetadataKeys.ReleaseGroup, parsed.ReleaseGroup,
            MetadataSources.Filename, ConfidenceFilename, now, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExtractContentAsync(
        FileInstanceId instanceId, FilePath path, IContentMetadataReader reader,
        DateTime now, CancellationToken cancellationToken)
    {
        try
        {
            var values = await reader.ReadAsync(path, cancellationToken).ConfigureAwait(false);
            foreach (var (key, value) in values)
            {
                await UpsertIfPresentAsync(instanceId, key, value,
                    reader.Source, reader.Confidence, now, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Lecture du contenu ({Source}) impossible pour {Path}", reader.Source, path);
        }
    }

    private async Task UpsertIfPresentAsync(
        FileInstanceId instanceId, string key, string? value,
        string source, double confidence, DateTime now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        await _metadata.UpsertAsync(new MetadataEntry(
            instanceId, key, value, source, confidence, now), cancellationToken).ConfigureAwait(false);
    }
}
