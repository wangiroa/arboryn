using Arboryn.Application.Abstractions;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.Replication;

/// <summary>
/// Annule un lot de réplication (Inc 10, § 5.8) en rejouant ses opérations complétées en ordre
/// inverse : une copie voit sa cible envoyée à la corbeille et son instance retirée ; un
/// rename/move est redéplacé à l'origine ; une suppression est restaurée depuis la corbeille.
/// </summary>
public sealed class UndoReplicationBatchHandler
{
    private readonly IOperationJournal _journal;
    private readonly IFileMover _mover;
    private readonly IRecycleBin _recycleBin;
    private readonly IFileInstanceRepository _instances;
    private readonly ILogger<UndoReplicationBatchHandler> _logger;

    public UndoReplicationBatchHandler(
        IOperationJournal journal,
        IFileMover mover,
        IRecycleBin recycleBin,
        IFileInstanceRepository instances,
        ILogger<UndoReplicationBatchHandler> logger)
    {
        _journal = journal;
        _mover = mover;
        _recycleBin = recycleBin;
        _instances = instances;
        _logger = logger;
    }

    public async Task<ReplicationUndoResult> ExecuteAsync(BatchId batchId, CancellationToken cancellationToken = default)
    {
        var operations = await _journal.GetBatchAsync(batchId, cancellationToken).ConfigureAwait(false);
        var completed = operations
            .Where(o => o.Status == OperationStatus.Completed)
            .Reverse()
            .ToList();

        int undone = 0, failed = 0;
        foreach (var op in completed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await UndoOneAsync(op, cancellationToken).ConfigureAwait(false);
                await _journal.MarkUndoneAsync(op.Id, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
                undone++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec de l'annulation de l'opération {Op} ({Kind})", op.Id.Value, op.Kind);
                failed++;
            }
        }

        return new ReplicationUndoResult(operations.Count > 0, undone, failed);
    }

    private async Task UndoOneAsync(Operation op, CancellationToken cancellationToken)
    {
        switch (op.Kind)
        {
            case OperationKind.Copy:
                // La cible copiée retourne à la corbeille et son instance est retirée du catalogue.
                if (op.NewPath is { } copyTarget)
                {
                    await _recycleBin.SendToRecycleBinAsync(copyTarget, cancellationToken).ConfigureAwait(false);
                }

                await _instances.MarkDeletedAsync(op.FileInstanceId, cancellationToken).ConfigureAwait(false);
                break;

            case OperationKind.Rename:
            case OperationKind.Move:
                if (op.NewPath is { } from && op.OldPath is { } to)
                {
                    await _mover.MoveAsync(from, to, cancellationToken).ConfigureAwait(false);
                    await _instances.UpdatePathAsync(op.FileInstanceId, to, cancellationToken).ConfigureAwait(false);
                }

                break;

            case OperationKind.Delete:
                // Restauration depuis la corbeille (new_path) vers l'emplacement d'origine (old_path).
                if (op.NewPath is { } recycled && op.OldPath is { } original)
                {
                    await _recycleBin.RestoreAsync(recycled, original, cancellationToken).ConfigureAwait(false);
                    await _instances.MarkActiveAsync(op.FileInstanceId, cancellationToken).ConfigureAwait(false);
                }

                break;
        }
    }
}

/// <summary>Bilan d'annulation d'un lot de réplication.</summary>
public sealed record ReplicationUndoResult(bool HadBatch, int Undone, int Failed);
