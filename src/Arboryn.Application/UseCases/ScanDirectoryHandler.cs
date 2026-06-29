using Arboryn.Application.Abstractions;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Metadata;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Scanne une racine et indexe chaque fichier comme FileInstance sur le volume donné.
/// Chaque instance est rattachée à un <see cref="LogicalFile"/> via sa signature
/// <c>name_size</c> (créé à la volée s'il n'existe pas).
/// </summary>
public sealed class ScanDirectoryHandler
{
    private readonly IFileScanner _scanner;
    private readonly IFileInstanceRepository _instanceRepository;
    private readonly ILogicalFileRepository _logicalFileRepository;
    private readonly LogicalFileResolver _logicalFileResolver;
    private readonly ExtractMetadataHandler _metadataExtractor;
    private readonly ILogger<ScanDirectoryHandler> _logger;

    public ScanDirectoryHandler(
        IFileScanner scanner,
        IFileInstanceRepository instanceRepository,
        ILogicalFileRepository logicalFileRepository,
        LogicalFileResolver logicalFileResolver,
        ExtractMetadataHandler metadataExtractor,
        ILogger<ScanDirectoryHandler> logger)
    {
        _scanner = scanner;
        _instanceRepository = instanceRepository;
        _logicalFileRepository = logicalFileRepository;
        _logicalFileResolver = logicalFileResolver;
        _metadataExtractor = metadataExtractor;
        _logger = logger;
    }

    public async Task<ScanResult> ExecuteAsync(
        FilePath rootPath,
        VolumeId volumeId,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var processed = 0;

        // Cache local des LogicalFiles déjà résolus pendant ce scan, pour limiter
        // les allers-retours base sur les signatures partagées (mêmes copies).
        var resolved = new Dictionary<string, LogicalFileId>(StringComparer.Ordinal);

        // Signatures dont la catégorie a déjà été affinée pendant ce scan : on n'affine
        // qu'une fois par LogicalFile (les instances d'une même signature partagent le contenu).
        var refinedSignatures = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var file in _scanner.ScanAsync(rootPath, volumeId, cancellationToken).ConfigureAwait(false))
        {
            var canonical = CanonicalName.From(file.Path.FileName);
            var signature = ContentSignature.NameSize(canonical, file.Size);
            var category = MediaClassifier.FromExtension(file.Path.Extension);
            var logicalFileId = await _logicalFileResolver
                .ResolveAsync(signature, category, resolved, cancellationToken).ConfigureAwait(false);

            var record = new FileInstanceRecord(
                FileInstanceId.New(),
                volumeId,
                file.Path,
                canonical,
                file.Size,
                file.ModifiedAt)
            {
                LogicalFileId = logicalFileId,
            };

            var actualId = await _instanceRepository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
            var refined = await _metadataExtractor
                .ExecuteAsync(actualId, record.Path, cancellationToken).ConfigureAwait(false);

            // Affine la catégorie du LogicalFile si le contenu le justifie (une fois par signature).
            if (refined != category && refinedSignatures.Add(signature.Value))
            {
                await _logicalFileRepository
                    .UpdateCategoryAsync(logicalFileId, refined, cancellationToken).ConfigureAwait(false);
            }

            if (++processed % ProgressEvery == 0)
            {
                progress?.Report(new ScanProgress(processed));
            }
        }

        progress?.Report(new ScanProgress(processed));
        _logger.LogInformation(
            "Indexation terminée : {Count} fichiers sur le volume {Volume} ; {LogicalFiles} LogicalFile(s) distincts",
            processed, volumeId, resolved.Count);
        return new ScanResult(processed);
    }

    private const int ProgressEvery = 100;
}

/// <summary>Avancement d'un scan, destiné à l'affichage UI.</summary>
public sealed record ScanProgress(int FilesProcessed);

/// <summary>Résultat agrégé d'un scan.</summary>
public sealed record ScanResult(int FilesProcessed);
