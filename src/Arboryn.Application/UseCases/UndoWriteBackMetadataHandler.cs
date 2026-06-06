using System.Text.Json;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Metadata;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Annule le dernier lot de write-back : réécrit dans chaque fichier ses métadonnées
/// d'origine (conservées en JSON lors de l'exécution), et marque l'opération comme annulée.
/// </summary>
public sealed class UndoWriteBackMetadataHandler
{
    private readonly IOperationJournal _journal;
    private readonly IReadOnlyList<IContentMetadataWriter> _writers;
    private readonly ILogger<UndoWriteBackMetadataHandler> _logger;

    public UndoWriteBackMetadataHandler(
        IOperationJournal journal,
        IEnumerable<IContentMetadataWriter> writers,
        ILogger<UndoWriteBackMetadataHandler> logger)
    {
        _journal = journal;
        _writers = writers.ToList();
        _logger = logger;
    }

    public async Task<UndoResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var batchId = await _journal.GetLastUndoableWriteBackBatchAsync(cancellationToken).ConfigureAwait(false);
        if (batchId is null)
        {
            return new UndoResult(HadBatch: false, Restored: 0, Failed: 0);
        }

        var operations = await _journal.GetBatchAsync(batchId.Value, cancellationToken).ConfigureAwait(false);

        var restored = 0;
        var failed = 0;

        foreach (var operation in operations
            .Where(o => o.Kind == OperationKind.MetadataWriteback
                && o.Status == OperationStatus.Completed
                && o.OldPath is not null
                && o.OldMetadataJson is not null)
            .Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = operation.OldPath!.Value;
            var category = MediaClassifier.FromExtension(path.Extension);
            var writer = _writers.FirstOrDefault(w => w.CanWrite(category));
            if (writer is null)
            {
                continue;
            }

            try
            {
                var previous = JsonSerializer.Deserialize<Dictionary<string, string?>>(operation.OldMetadataJson!)
                    ?? new Dictionary<string, string?>();
                await writer.WriteAsync(path, previous, cancellationToken).ConfigureAwait(false);
                await _journal.MarkUndoneAsync(operation.Id, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
                restored++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Annulation du write-back impossible pour {Path}", path);
                failed++;
            }
        }

        return new UndoResult(HadBatch: true, Restored: restored, Failed: failed);
    }
}
