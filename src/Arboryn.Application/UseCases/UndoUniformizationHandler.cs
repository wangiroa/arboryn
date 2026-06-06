using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Annule le dernier lot d'uniformisation : remet chaque fichier de son chemin canonique
/// vers son emplacement d'origine (rejouage inverse), restaure le chemin en base, et marque
/// l'opération comme annulée. Les opérations sont rejouées en ordre inverse.
/// </summary>
public sealed class UndoUniformizationHandler
{
    private readonly IOperationJournal _journal;
    private readonly IFileMover _mover;
    private readonly IFileInstanceRepository _instances;
    private readonly ILogger<UndoUniformizationHandler> _logger;

    public UndoUniformizationHandler(
        IOperationJournal journal,
        IFileMover mover,
        IFileInstanceRepository instances,
        ILogger<UndoUniformizationHandler> logger)
    {
        _journal = journal;
        _mover = mover;
        _instances = instances;
        _logger = logger;
    }

    public async Task<UndoResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var batchId = await _journal.GetLastUndoableUniformizationBatchAsync(cancellationToken).ConfigureAwait(false);
        if (batchId is null)
        {
            return new UndoResult(HadBatch: false, Restored: 0, Failed: 0);
        }

        var operations = await _journal.GetBatchAsync(batchId.Value, cancellationToken).ConfigureAwait(false);

        var restored = 0;
        var failed = 0;

        foreach (var operation in operations
            .Where(o => o.Kind is OperationKind.Move or OperationKind.Rename
                && o.Status == OperationStatus.Completed
                && o.OldPath is not null && o.NewPath is not null)
            .Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _mover.MoveAsync(operation.NewPath!.Value, operation.OldPath!.Value, cancellationToken).ConfigureAwait(false);
                await _instances.UpdatePathAsync(operation.FileInstanceId, operation.OldPath.Value, cancellationToken).ConfigureAwait(false);
                await _journal.MarkUndoneAsync(operation.Id, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
                restored++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Annulation impossible pour {Path}", operation.NewPath!.Value);
                failed++;
            }
        }

        return new UndoResult(HadBatch: true, Restored: restored, Failed: failed);
    }
}
