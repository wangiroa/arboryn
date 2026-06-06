using Arboryn.Application.Abstractions;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Exécute un <see cref="UniformizationPlan"/> : déplace/renomme chaque fichier vers son
/// chemin canonique, met à jour l'instance en base, et journalise chaque opération sous un
/// même <see cref="BatchId"/> pour permettre l'annulation. Les échecs individuels sont isolés.
/// </summary>
public sealed class ExecuteUniformizationHandler
{
    private readonly IFileMover _mover;
    private readonly IFileInstanceRepository _instances;
    private readonly IOperationJournal _journal;
    private readonly ILogger<ExecuteUniformizationHandler> _logger;

    public ExecuteUniformizationHandler(
        IFileMover mover,
        IFileInstanceRepository instances,
        IOperationJournal journal,
        ILogger<ExecuteUniformizationHandler> logger)
    {
        _mover = mover;
        _instances = instances;
        _journal = journal;
        _logger = logger;
    }

    public async Task<UniformizationResult> ExecuteAsync(
        UniformizationPlan plan, CancellationToken cancellationToken = default)
    {
        var batchId = BatchId.New();
        var moved = 0;
        var failed = 0;

        foreach (var operation in plan.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _mover.MoveAsync(operation.OldPath, operation.NewPath, cancellationToken).ConfigureAwait(false);
                await _instances.UpdatePathAsync(operation.Id, operation.NewPath, cancellationToken).ConfigureAwait(false);

                var now = DateTime.UtcNow;
                await _journal.AppendAsync(new Operation(
                    OperationId.New(), batchId, operation.Kind, operation.Id,
                    operation.OldPath, operation.NewPath, OperationStatus.Completed, now, now),
                    cancellationToken).ConfigureAwait(false);

                moved++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec de l'uniformisation de {Old} → {New}", operation.OldPath, operation.NewPath);

                await _journal.AppendAsync(new Operation(
                    OperationId.New(), batchId, operation.Kind, operation.Id,
                    operation.OldPath, operation.NewPath, OperationStatus.Failed, DateTime.UtcNow),
                    cancellationToken).ConfigureAwait(false);

                failed++;
            }
        }

        return new UniformizationResult(batchId, moved, failed);
    }
}

/// <summary>Résultat d'une exécution d'uniformisation en lot.</summary>
public sealed record UniformizationResult(BatchId BatchId, int Moved, int Failed);
