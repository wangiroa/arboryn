using Arboryn.Domain.Entities;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>Journal persistant des opérations fichier, base de l'annulation (undo).</summary>
public interface IOperationJournal
{
    Task AppendAsync(Operation operation, CancellationToken cancellationToken);

    /// <summary>Identifiant du dernier lot de suppressions exécutées non encore annulées.</summary>
    Task<BatchId?> GetLastUndoableDeleteBatchAsync(CancellationToken cancellationToken);

    /// <summary>Identifiant du dernier lot d'uniformisation (rename/move) exécuté non encore annulé.</summary>
    Task<BatchId?> GetLastUndoableUniformizationBatchAsync(CancellationToken cancellationToken);

    /// <summary>Identifiant du dernier lot de write-back de métadonnées exécuté non encore annulé.</summary>
    Task<BatchId?> GetLastUndoableWriteBackBatchAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Operation>> GetBatchAsync(BatchId batchId, CancellationToken cancellationToken);

    /// <summary>
    /// Opérations les plus récentes (tous types confondus), les plus récentes d'abord, pour
    /// l'écran Historique. Bornées par <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<Operation>> GetRecentAsync(int limit, CancellationToken cancellationToken);

    Task MarkUndoneAsync(OperationId operationId, DateTime undoneAt, CancellationToken cancellationToken);

    /// <summary>
    /// Opérations de réplication (Inc 10) en attente (statut <c>pending</c>, volume source/cible
    /// renseigné) : celles différées faute de volume connecté, à reprendre au rebranchement.
    /// </summary>
    Task<IReadOnlyList<Operation>> GetPendingReplicationOperationsAsync(CancellationToken cancellationToken);

    /// <summary>Met à jour le statut d'une opération (et sa date d'exécution si fournie).</summary>
    Task SetStatusAsync(
        OperationId operationId, Arboryn.Domain.Enums.OperationStatus status,
        DateTime? executedAt, CancellationToken cancellationToken);
}
