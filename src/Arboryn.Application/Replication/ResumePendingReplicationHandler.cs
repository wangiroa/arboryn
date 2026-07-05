using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Replication;

/// <summary>
/// Reprend les opérations de réplication différées (Inc 10, § 5.5 étape 6) : celles marquées
/// <c>pending</c> faute de volume connecté. Pour chacune dont tous les volumes requis sont
/// désormais en ligne, elle est exécutée puis la placeholder en attente est supersédée. À appeler
/// au démarrage et au branchement d'un volume.
/// </summary>
public sealed class ResumePendingReplicationHandler
{
    private readonly IOperationJournal _journal;
    private readonly IVolumeRepository _volumes;
    private readonly ReplicationOperationExecutor _executor;

    public ResumePendingReplicationHandler(
        IOperationJournal journal,
        IVolumeRepository volumes,
        ReplicationOperationExecutor executor)
    {
        _journal = journal;
        _volumes = volumes;
        _executor = executor;
    }

    public async Task<ResumePendingResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _journal.GetPendingReplicationOperationsAsync(cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return new ResumePendingResult(0, 0, 0);
        }

        var volumes = await _volumes.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var online = volumes
            .Where(v => v.Status == VolumeStatus.Online && !string.IsNullOrEmpty(v.MountPoint))
            .Select(v => v.Id)
            .ToHashSet();

        int resumed = 0, stillPending = 0, failed = 0;
        foreach (var op in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var required = new List<VolumeId>();
            if (op.SourceVolumeId is { } src)
            {
                required.Add(src);
            }

            if (op.Kind == OperationKind.Copy && op.TargetVolumeId is { } tgt)
            {
                required.Add(tgt);
            }

            if (required.Count == 0 || required.Any(v => !online.Contains(v)))
            {
                stillPending++;
                continue;
            }

            if (await _executor.TryResumeAsync(op, cancellationToken).ConfigureAwait(false))
            {
                resumed++;
            }
            else
            {
                failed++;
            }
        }

        return new ResumePendingResult(resumed, stillPending, failed);
    }
}

/// <summary>Bilan d'une reprise d'opérations différées.</summary>
public sealed record ResumePendingResult(int Resumed, int StillPending, int Failed);
