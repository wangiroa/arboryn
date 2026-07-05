using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.Replication;

/// <summary>
/// Exécute un <see cref="PlacementPlan"/> (Inc 10, § 5.5 étape 6) sous un même lot transactionnel.
/// Re-absolutise les chemins relatifs via la racine (<c>mount_point</c>) de chaque volume, exécute
/// chaque opération (rename/move intra-volume, copy inter-volume vérifiée, delete corbeille) et
/// journalise. Les opérations dont un volume requis est hors-ligne sont marquées <c>pending</c> et
/// reprises au rebranchement (cf. <see cref="ResumePendingReplicationHandler"/>).
/// </summary>
public sealed class ExecuteReplicationPlanHandler
{
    private readonly IVolumeRepository _volumes;
    private readonly ReplicationOperationExecutor _executor;
    private readonly ILogger<ExecuteReplicationPlanHandler> _logger;

    public ExecuteReplicationPlanHandler(
        IVolumeRepository volumes,
        ReplicationOperationExecutor executor,
        ILogger<ExecuteReplicationPlanHandler> logger)
    {
        _volumes = volumes;
        _executor = executor;
        _logger = logger;
    }

    public async Task<ReplicationExecutionResult> ExecuteAsync(
        PlacementPlan plan, CancellationToken cancellationToken = default)
    {
        var volumes = await _volumes.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var roots = volumes.ToDictionary(v => v.Id, v => v.MountPoint);
        var online = volumes
            .Where(v => v.Status == VolumeStatus.Online && !string.IsNullOrEmpty(v.MountPoint))
            .Select(v => v.Id)
            .ToHashSet();

        var batchId = BatchId.New();
        int copied = 0, moved = 0, deleted = 0, pending = 0, failed = 0;

        foreach (var op in plan.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var required = op.Kind == OperationKind.Copy
                ? new[] { op.SourceVolumeId, op.TargetVolumeId }
                : new[] { op.SourceVolumeId };

            if (required.Any(v => !roots.TryGetValue(v, out var root) || string.IsNullOrEmpty(root)))
            {
                await _executor.JournalMissingVolumeAsync(op, batchId, cancellationToken).ConfigureAwait(false);
                failed++;
                continue;
            }

            if (required.Any(v => !online.Contains(v)))
            {
                await _executor.JournalPlannedPendingAsync(op, roots, batchId, cancellationToken).ConfigureAwait(false);
                pending++;
                continue;
            }

            try
            {
                var kind = await _executor.ExecutePlannedAsync(op, roots, batchId, cancellationToken).ConfigureAwait(false);
                switch (kind)
                {
                    case OperationKind.Copy: copied++; break;
                    case OperationKind.Delete: deleted++; break;
                    default: moved++; break; // rename / move
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec de l'opération de réplication {Kind} sur {LogicalFile}", op.Kind, op.LogicalFileId.Value);
                await _executor.JournalPlannedFailedAsync(op, roots, batchId, cancellationToken).ConfigureAwait(false);
                failed++;
            }
        }

        return new ReplicationExecutionResult(batchId, copied, moved, deleted, pending, failed);
    }
}

/// <summary>Bilan d'exécution d'un plan de réplication.</summary>
public sealed record ReplicationExecutionResult(
    BatchId BatchId, int Copied, int Moved, int Deleted, int Pending, int Failed);
