using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Metadata;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Re-scan incrémental d'un volume (Inc 9). Deux chemins :
/// <list type="bullet">
///   <item><b>USN Journal</b> (NTFS, rapide) : ne traite que les fichiers signalés modifiés
///   depuis la dernière position USN, sans réénumérer l'arborescence. Les suppressions du
///   journal marquent les instances « manquantes ».</item>
///   <item><b>Parcours mtime</b> (universel, repli) : énumère l'arborescence, saute l'extraction
///   de métadonnées des fichiers inchangés (même taille + date), traite les nouveaux/modifiés,
///   puis marque « manquantes » les instances actives non revues.</item>
/// </list>
/// Dans les deux cas, la position de scan (et l'USN courant si disponible) est mémorisée pour
/// rendre le scan suivant incrémental. Un premier scan (sans référence USN) passe par le parcours
/// complet, ce qui équivaut à l'indexation initiale.
/// </summary>
public sealed class RescanVolumeHandler
{
    private readonly IFileScanner _scanner;
    private readonly IFileInstanceRepository _instances;
    private readonly ILogicalFileRepository _logicalFiles;
    private readonly LogicalFileResolver _resolver;
    private readonly ExtractMetadataHandler _metadataExtractor;
    private readonly IVolumeRepository _volumes;
    private readonly IUsnJournalReader _usn;
    private readonly ILogger<RescanVolumeHandler> _logger;

    // Tolérance de comparaison des dates de modification (perte de précision possible
    // au stockage ISO / entre systèmes de fichiers).
    private static readonly TimeSpan MTimeTolerance = TimeSpan.FromSeconds(2);

    public RescanVolumeHandler(
        IFileScanner scanner,
        IFileInstanceRepository instances,
        ILogicalFileRepository logicalFiles,
        LogicalFileResolver resolver,
        ExtractMetadataHandler metadataExtractor,
        IVolumeRepository volumes,
        IUsnJournalReader usn,
        ILogger<RescanVolumeHandler> logger)
    {
        _scanner = scanner;
        _instances = instances;
        _logicalFiles = logicalFiles;
        _resolver = resolver;
        _metadataExtractor = metadataExtractor;
        _volumes = volumes;
        _usn = usn;
        _logger = logger;
    }

    public async Task<RescanResult> ExecuteAsync(
        FilePath rootPath,
        VolumeRecord volume,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var changeSet = await _usn.TryReadChangesAsync(volume, rootPath, cancellationToken).ConfigureAwait(false);
        var result = changeSet is not null
            ? await IncrementalAsync(rootPath, volume, changeSet, progress, cancellationToken).ConfigureAwait(false)
            : await FullAsync(rootPath, volume, progress, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Re-scan du volume {Volume} ({Mode}) : {Processed} traité(s), {Missing} manquant(s)",
            volume.Name, result.UsedUsnJournal ? "USN" : "mtime", result.Processed, result.Missing);

        return result;
    }

    /// <summary>Parcours complet (repli) : énumère, saute les inchangés, marque les manquants.</summary>
    private async Task<RescanResult> FullAsync(
        FilePath rootPath, VolumeRecord volume, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var existing = await LoadExistingAsync(rootPath, volume.Id, cancellationToken).ConfigureAwait(false);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var caches = new ScanCaches();
        var processed = 0;

        await foreach (var file in _scanner.ScanAsync(rootPath, volume.Id, cancellationToken).ConfigureAwait(false))
        {
            var key = Key(file.Path);
            seen.Add(key);

            if (existing.TryGetValue(key, out var prev) && IsUnchanged(prev, file.Size, file.ModifiedAt))
            {
                continue;
            }

            await ProcessChangedAsync(volume.Id, file.Path, file.Size, file.ModifiedAt, caches, cancellationToken).ConfigureAwait(false);
            if (++processed % ProgressEvery == 0)
            {
                progress?.Report(new ScanProgress(processed));
            }
        }

        var missing = 0;
        foreach (var (key, prev) in existing)
        {
            if (!seen.Contains(key))
            {
                await _instances.MarkMissingAsync(prev.Id, cancellationToken).ConfigureAwait(false);
                missing++;
            }
        }

        progress?.Report(new ScanProgress(processed));
        var position = await _usn.TryGetCurrentPositionAsync(volume, cancellationToken).ConfigureAwait(false);
        await _volumes.RecordScanAsync(volume.Id, DateTime.UtcNow, position, cancellationToken).ConfigureAwait(false);
        return new RescanResult(processed, missing, UsedUsnJournal: false);
    }

    /// <summary>Chemin rapide USN : ne traite que les chemins signalés par le journal.</summary>
    private async Task<RescanResult> IncrementalAsync(
        FilePath rootPath, VolumeRecord volume, UsnChangeSet changeSet,
        IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var existing = await LoadExistingAsync(rootPath, volume.Id, cancellationToken).ConfigureAwait(false);
        var caches = new ScanCaches();
        var processed = 0;
        var missing = 0;

        foreach (var change in changeSet.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Key(change.Path);

            // Suppression journalisée, ou fichier désormais absent : marque l'instance manquante.
            var stat = change.Deleted ? null : _scanner.TryStat(change.Path);
            if (stat is null)
            {
                if (existing.TryGetValue(key, out var gone))
                {
                    await _instances.MarkMissingAsync(gone.Id, cancellationToken).ConfigureAwait(false);
                    missing++;
                }

                continue;
            }

            if (existing.TryGetValue(key, out var prev) && IsUnchanged(prev, stat.Size, stat.ModifiedAt))
            {
                continue;
            }

            await ProcessChangedAsync(volume.Id, change.Path, stat.Size, stat.ModifiedAt, caches, cancellationToken).ConfigureAwait(false);
            if (++processed % ProgressEvery == 0)
            {
                progress?.Report(new ScanProgress(processed));
            }
        }

        progress?.Report(new ScanProgress(processed));
        await _volumes.RecordScanAsync(volume.Id, DateTime.UtcNow, changeSet.NextUsn, cancellationToken).ConfigureAwait(false);
        return new RescanResult(processed, missing, UsedUsnJournal: true);
    }

    /// <summary>Indexe (ou réindexe) un fichier nouveau ou modifié : rattachement LF + métadonnées.</summary>
    private async Task ProcessChangedAsync(
        VolumeId volumeId, FilePath path, long size, DateTime modifiedAt, ScanCaches caches, CancellationToken cancellationToken)
    {
        var canonical = CanonicalName.From(path.FileName);
        var signature = ContentSignature.NameSize(canonical, size);
        var category = MediaClassifier.FromExtension(path.Extension);
        var logicalFileId = await _resolver
            .ResolveAsync(signature, category, caches.Resolved, cancellationToken).ConfigureAwait(false);

        var record = new FileInstanceRecord(FileInstanceId.New(), volumeId, path, canonical, size, modifiedAt)
        {
            LogicalFileId = logicalFileId,
        };

        var actualId = await _instances.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        var refined = await _metadataExtractor.ExecuteAsync(actualId, path, cancellationToken).ConfigureAwait(false);

        if (refined != category && caches.RefinedSignatures.Add(signature.Value))
        {
            await _logicalFiles.UpdateCategoryAsync(logicalFileId, refined, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Dictionary<string, ExistingInstance>> LoadExistingAsync(
        FilePath rootPath, VolumeId volumeId, CancellationToken cancellationToken)
    {
        var records = await _instances.GetActiveInstancesAsync(volumeId, rootPath, cancellationToken).ConfigureAwait(false);
        var map = new Dictionary<string, ExistingInstance>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            map[Key(record.Path)] = new ExistingInstance(record.Id, record.Size, record.ModifiedAt);
        }

        return map;
    }

    private static bool IsUnchanged(ExistingInstance prev, long size, DateTime modifiedAt)
        => prev.Size == size && (prev.ModifiedAt - modifiedAt).Duration() <= MTimeTolerance;

    private static string Key(FilePath path) => path.Value.ToLowerInvariant();

    private const int ProgressEvery = 100;

    private readonly record struct ExistingInstance(FileInstanceId Id, long Size, DateTime ModifiedAt);

    /// <summary>Caches partagés sur la durée d'un re-scan.</summary>
    private sealed class ScanCaches
    {
        public Dictionary<string, LogicalFileId> Resolved { get; } = new(StringComparer.Ordinal);

        public HashSet<string> RefinedSignatures { get; } = new(StringComparer.Ordinal);
    }
}

/// <summary>Résultat d'un re-scan incrémental.</summary>
/// <param name="Processed">Nombre de fichiers nouveaux ou modifiés indexés.</param>
/// <param name="Missing">Nombre d'instances marquées manquantes (fichiers disparus).</param>
/// <param name="UsedUsnJournal"><c>true</c> si le chemin rapide USN a été emprunté.</param>
public sealed record RescanResult(int Processed, int Missing, bool UsedUsnJournal);
