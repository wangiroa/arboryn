using Arboryn.Domain.Entities;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>Journal persistant des opérations fichier, base de l'annulation (undo).</summary>
public interface IOperationJournal
{
    Task AppendAsync(Operation operation, CancellationToken cancellationToken);

    /// <summary>Identifiant du dernier lot de suppressions exécutées non encore annulées.</summary>
    Task<BatchId?> GetLastUndoableDeleteBatchAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Operation>> GetBatchAsync(BatchId batchId, CancellationToken cancellationToken);

    Task MarkUndoneAsync(OperationId operationId, DateTime undoneAt, CancellationToken cancellationToken);
}
