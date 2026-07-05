using Arboryn.Domain.Replication;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Un périmètre de réplication nommé (Inc 10) : une <see cref="ScopeExpression"/> réutilisable,
/// que l'on rattache à un ou plusieurs volumes via <c>VolumeRecord.ReplicationScopeId</c>.
/// </summary>
public sealed record ReplicationScope(ScopeId Id, string Name, ScopeExpression Expression);

/// <summary>Dépôt des périmètres de réplication (table <c>replication_scopes</c>).</summary>
public interface IReplicationScopeRepository
{
    Task<ReplicationScope?> GetAsync(ScopeId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReplicationScope>> GetAllAsync(CancellationToken cancellationToken);

    Task UpsertAsync(ReplicationScope scope, CancellationToken cancellationToken);

    Task DeleteAsync(ScopeId id, CancellationToken cancellationToken);
}
