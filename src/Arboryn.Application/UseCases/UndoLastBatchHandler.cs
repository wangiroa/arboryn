using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Annule le dernier lot de suppressions : restaure chaque fichier depuis la
/// corbeille vers son emplacement d'origine, réactive l'instance et marque
/// l'opération comme annulée.
/// </summary>
public sealed class UndoLastBatchHandler
{
    private readonly IOperationJournal _journal;
    private readonly IRecycleBin _recycleBin;
    private readonly IFileInstanceRepository _repository;
    private readonly ILogger<UndoLastBatchHandler> _logger;

    public UndoLastBatchHandler(
        IOperationJournal journal,
        IRecycleBin recycleBin,
        IFileInstanceRepository repository,
        ILogger<UndoLastBatchHandler> logger)
    {
        _journal = journal;
        _recycleBin = recycleBin;
        _repository = repository;
        _logger = logger;
    }

    public async Task<UndoResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var batchId = await _journal.GetLastUndoableDeleteBatchAsync(cancellationToken).ConfigureAwait(false);
        if (batchId is null)
        {
            return new UndoResult(HadBatch: false, Restored: 0, Failed: 0);
        }

        var operations = await _journal.GetBatchAsync(batchId.Value, cancellationToken).ConfigureAwait(false);

        var restored = 0;
        var failed = 0;

        foreach (var operation in operations)
        {
            if (operation.Kind != OperationKind.Delete ||
                operation.Status != OperationStatus.Completed ||
                operation.OldPath is null ||
                operation.NewPath is null)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var ok = await _recycleBin.RestoreAsync(
                operation.NewPath.Value, operation.OldPath.Value, cancellationToken).ConfigureAwait(false);

            if (ok)
            {
                await _repository.MarkActiveAsync(operation.FileInstanceId, cancellationToken).ConfigureAwait(false);
                await _journal.MarkUndoneAsync(operation.Id, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
                restored++;
            }
            else
            {
                _logger.LogWarning("Restauration impossible pour {Path}", operation.OldPath.Value);
                failed++;
            }
        }

        return new UndoResult(HadBatch: true, Restored: restored, Failed: failed);
    }
}

/// <summary>Résultat d'une annulation de lot.</summary>
public sealed record UndoResult(bool HadBatch, int Restored, int Failed);
