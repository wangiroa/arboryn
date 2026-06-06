using System.Text.Json;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Metadata;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Réécrit dans chaque fichier (write-back) ses métadonnées canoniques fusionnées, via le
/// writer adapté à sa catégorie. Journalise chaque opération <c>metadata_writeback</c> en
/// conservant les valeurs d'origine (JSON) pour permettre l'annulation. Les échecs sont isolés.
/// </summary>
public sealed class WriteBackMetadataHandler
{
    private readonly IReadOnlyList<IContentMetadataWriter> _writers;
    private readonly IFileInstanceRepository _instances;
    private readonly IFileMetadataRepository _metadata;
    private readonly IOperationJournal _journal;
    private readonly ILogger<WriteBackMetadataHandler> _logger;

    public WriteBackMetadataHandler(
        IEnumerable<IContentMetadataWriter> writers,
        IFileInstanceRepository instances,
        IFileMetadataRepository metadata,
        IOperationJournal journal,
        ILogger<WriteBackMetadataHandler> logger)
    {
        _writers = writers.ToList();
        _instances = instances;
        _metadata = metadata;
        _journal = journal;
        _logger = logger;
    }

    public async Task<WriteBackResult> ExecuteAsync(
        VolumeId volumeId, FilePath? underRoot = null, CancellationToken cancellationToken = default)
    {
        var instances = await _instances
            .GetActiveInstancesAsync(volumeId, underRoot, cancellationToken).ConfigureAwait(false);

        var batchId = BatchId.New();
        var written = 0;
        var failed = 0;

        foreach (var instance in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var category = MediaClassifier.FromExtension(instance.Path.Extension);
            var writer = _writers.FirstOrDefault(w => w.CanWrite(category));
            if (writer is null)
            {
                continue;
            }

            var fused = await _metadata.GetFusedAsync(instance.Id, cancellationToken).ConfigureAwait(false);
            var fields = fused
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.Value))
                .ToDictionary(kv => kv.Key, kv => (string?)kv.Value.Value, StringComparer.Ordinal);
            if (fields.Count == 0)
            {
                continue;
            }

            try
            {
                var previous = await writer.WriteAsync(instance.Path, fields, cancellationToken).ConfigureAwait(false);
                if (previous.Count == 0)
                {
                    continue;
                }

                var now = DateTime.UtcNow;
                await _journal.AppendAsync(new Operation(
                    OperationId.New(), batchId, OperationKind.MetadataWriteback, instance.Id,
                    OldPath: instance.Path, NewPath: instance.Path, OperationStatus.Completed,
                    CreatedAt: now, ExecutedAt: now,
                    OldMetadataJson: JsonSerializer.Serialize(previous)), cancellationToken).ConfigureAwait(false);

                written++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec du write-back pour {Path}", instance.Path);

                await _journal.AppendAsync(new Operation(
                    OperationId.New(), batchId, OperationKind.MetadataWriteback, instance.Id,
                    OldPath: instance.Path, NewPath: instance.Path, OperationStatus.Failed,
                    CreatedAt: DateTime.UtcNow), cancellationToken).ConfigureAwait(false);

                failed++;
            }
        }

        return new WriteBackResult(batchId, written, failed);
    }
}

/// <summary>Résultat d'un write-back en lot.</summary>
public sealed record WriteBackResult(BatchId BatchId, int Written, int Failed);
