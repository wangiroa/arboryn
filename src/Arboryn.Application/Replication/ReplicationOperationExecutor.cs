using System.IO;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.Replication;

/// <summary>
/// Exécute une opération de placement unitaire (Inc 10, § 5.6/5.8) et la journalise. Partagé
/// par l'exécution du plan, la reprise des opérations différées et — pour la re-absolutisation —
/// la conversion des chemins relatifs (du plan) en chemins absolus via la racine de chaque volume.
/// Les copies inter-volume sont vérifiées par comparaison d'empreinte (intégrité post-copie).
/// </summary>
public sealed class ReplicationOperationExecutor
{
    private readonly IFileMover _mover;
    private readonly IRecycleBin _recycleBin;
    private readonly IFileHasher _hasher;
    private readonly IFileScanner _scanner;
    private readonly IFileInstanceRepository _instances;
    private readonly IFileInstanceLinker _linker;
    private readonly IOperationJournal _journal;
    private readonly ILogger<ReplicationOperationExecutor> _logger;

    public ReplicationOperationExecutor(
        IFileMover mover,
        IRecycleBin recycleBin,
        IFileHasher hasher,
        IFileScanner scanner,
        IFileInstanceRepository instances,
        IFileInstanceLinker linker,
        IOperationJournal journal,
        ILogger<ReplicationOperationExecutor> logger)
    {
        _mover = mover;
        _recycleBin = recycleBin;
        _hasher = hasher;
        _scanner = scanner;
        _instances = instances;
        _linker = linker;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>Exécute une opération planifiée (chemins relatifs + racines) et journalise le succès. Lève en cas d'échec.</summary>
    public Task<OperationKind> ExecutePlannedAsync(
        PlacementOperation op, IReadOnlyDictionary<VolumeId, string?> roots, BatchId batchId, CancellationToken cancellationToken)
        => ExecuteResolvedAsync(Resolve(op, roots), op.LogicalFileId, batchId, cancellationToken);

    /// <summary>Journalise une opération différée (volume hors-ligne) avec ses chemins absolus et ses volumes.</summary>
    public Task JournalPlannedPendingAsync(
        PlacementOperation op, IReadOnlyDictionary<VolumeId, string?> roots, BatchId batchId, CancellationToken cancellationToken)
    {
        var r = Resolve(op, roots);
        return AppendAsync(op.Kind, InstanceOf(op), r.Source, r.Target, op.SourceVolumeId, op.TargetVolumeId,
            op.LogicalFileId.Value, OperationStatus.Pending, executedAt: null, batchId, cancellationToken);
    }

    /// <summary>Journalise une opération échouée (chemins connus).</summary>
    public Task JournalPlannedFailedAsync(
        PlacementOperation op, IReadOnlyDictionary<VolumeId, string?> roots, BatchId batchId, CancellationToken cancellationToken)
    {
        var r = Resolve(op, roots);
        return AppendAsync(op.Kind, InstanceOf(op), r.Source, r.Target, op.SourceVolumeId, op.TargetVolumeId,
            op.LogicalFileId.Value, OperationStatus.Failed, executedAt: null, batchId, cancellationToken);
    }

    /// <summary>Journalise une opération impossible à résoudre (racine de volume inconnue).</summary>
    public Task JournalMissingVolumeAsync(PlacementOperation op, BatchId batchId, CancellationToken cancellationToken)
        => AppendAsync(op.Kind, InstanceOf(op), null, null, op.SourceVolumeId, op.TargetVolumeId,
            op.LogicalFileId.Value, OperationStatus.Failed, executedAt: null, batchId, cancellationToken);

    /// <summary>
    /// Reprend une opération différée persistée (chemins déjà absolus) : l'exécute en rattachant
    /// l'opération réussie à son lot d'origine, puis marque la placeholder <c>pending</c> comme
    /// annulée (superseded). Renvoie <c>false</c> et laisse l'opération en attente en cas d'échec.
    /// </summary>
    public async Task<bool> TryResumeAsync(Operation pending, CancellationToken cancellationToken)
    {
        if (pending.OldPath is not { } source || pending.SourceVolumeId is not { } sourceVolume
            || pending.TargetVolumeId is not { } targetVolume)
        {
            return false;
        }

        var target = pending.NewPath ?? source;
        var resolved = new ResolvedOp(pending.Kind, pending.FileInstanceId, sourceVolume, targetVolume, source, target);
        var logicalFileId = pending.OldMetadataJson is { Length: > 0 } id ? new LogicalFileId(id) : (LogicalFileId?)null;

        try
        {
            await ExecuteResolvedAsync(resolved, logicalFileId, pending.BatchId, cancellationToken).ConfigureAwait(false);
            await _journal.SetStatusAsync(pending.Id, OperationStatus.Cancelled, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de la reprise de l'opération {Op} ({Kind})", pending.Id.Value, pending.Kind);
            return false;
        }
    }

    private async Task<OperationKind> ExecuteResolvedAsync(
        ResolvedOp r, LogicalFileId? logicalFileId, BatchId batchId, CancellationToken cancellationToken)
    {
        switch (r.Kind)
        {
            case OperationKind.Rename:
            case OperationKind.Move:
                await _mover.MoveAsync(r.Source, r.Target, cancellationToken).ConfigureAwait(false);
                await _instances.UpdatePathAsync(r.InstanceId!.Value, r.Target, cancellationToken).ConfigureAwait(false);
                await AppendAsync(r.Kind, r.InstanceId!.Value, r.Source, r.Target, r.SourceVolumeId, r.TargetVolumeId,
                    null, OperationStatus.Completed, DateTime.UtcNow, batchId, cancellationToken).ConfigureAwait(false);
                break;

            case OperationKind.Delete:
                var recycled = await _recycleBin.SendToRecycleBinAsync(r.Source, cancellationToken).ConfigureAwait(false);
                await _instances.MarkDeletedAsync(r.InstanceId!.Value, cancellationToken).ConfigureAwait(false);
                await AppendAsync(OperationKind.Delete, r.InstanceId!.Value, r.Source, recycled, r.SourceVolumeId, r.TargetVolumeId,
                    null, OperationStatus.Completed, DateTime.UtcNow, batchId, cancellationToken).ConfigureAwait(false);
                break;

            case OperationKind.Copy:
                await ExecuteCopyAsync(r, logicalFileId, batchId, cancellationToken).ConfigureAwait(false);
                break;

            default:
                throw new NotSupportedException($"Opération de réplication non gérée : {r.Kind}");
        }

        return r.Kind;
    }

    private async Task ExecuteCopyAsync(
        ResolvedOp r, LogicalFileId? logicalFileId, BatchId batchId, CancellationToken cancellationToken)
    {
        await _mover.CopyAsync(r.Source, r.Target, cancellationToken).ConfigureAwait(false);

        // Vérification d'intégrité post-copie : la cible doit être bit-à-bit identique à la source.
        var sourceHash = await _hasher.ComputeAsync(r.Source, cancellationToken).ConfigureAwait(false);
        var targetHash = await _hasher.ComputeAsync(r.Target, cancellationToken).ConfigureAwait(false);
        if (!sourceHash.Equals(targetHash))
        {
            await _recycleBin.SendToRecycleBinAsync(r.Target, cancellationToken).ConfigureAwait(false);
            throw new ReplicationIntegrityException(r.Source, r.Target);
        }

        var size = _scanner.TryStat(r.Target)?.Size ?? 0;
        var newInstanceId = FileInstanceId.New();
        await _instances.UpsertAsync(
            new FileInstanceRecord(
                newInstanceId, r.TargetVolumeId, r.Target,
                CanonicalName.From(Path.GetFileName(r.Target.Value)), size, DateTime.UtcNow),
            cancellationToken).ConfigureAwait(false);
        if (logicalFileId is { } lf)
        {
            await _linker.SetLogicalFileAsync(newInstanceId, lf, cancellationToken).ConfigureAwait(false);
        }

        // file_instance_id = nouvelle instance cible (pour l'undo) ; old_metadata_json = LogicalFile
        // (pour rattacher la cible en cas de reprise différée).
        await AppendAsync(OperationKind.Copy, newInstanceId, r.Source, r.Target, r.SourceVolumeId, r.TargetVolumeId,
            logicalFileId?.Value, OperationStatus.Completed, DateTime.UtcNow, batchId, cancellationToken).ConfigureAwait(false);
    }

    private Task AppendAsync(
        OperationKind kind, FileInstanceId instanceId, FilePath? oldPath, FilePath? newPath,
        VolumeId sourceVolume, VolumeId targetVolume, string? metadata,
        OperationStatus status, DateTime? executedAt, BatchId batchId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return _journal.AppendAsync(new Operation(
            OperationId.New(), batchId, kind, instanceId, oldPath, newPath, status,
            CreatedAt: now, ExecutedAt: executedAt, UndoneAt: null,
            OldMetadataJson: metadata, SourceVolumeId: sourceVolume, TargetVolumeId: targetVolume),
            cancellationToken);
    }

    private static FileInstanceId InstanceOf(PlacementOperation op)
        => op.InstanceId ?? throw new InvalidOperationException($"Opération {op.Kind} sans instance de référence.");

    private static ResolvedOp Resolve(PlacementOperation op, IReadOnlyDictionary<VolumeId, string?> roots)
    {
        var source = FilePath.From(Combine(roots[op.SourceVolumeId]!, op.OldRelativePath!));
        var target = FilePath.From(Combine(roots[op.TargetVolumeId]!, op.NewRelativePath));
        return new ResolvedOp(op.Kind, op.InstanceId, op.SourceVolumeId, op.TargetVolumeId, source, target);
    }

    private static string Combine(string root, string relative) => Path.Combine(root, relative);

    private readonly record struct ResolvedOp(
        OperationKind Kind,
        FileInstanceId? InstanceId,
        VolumeId SourceVolumeId,
        VolumeId TargetVolumeId,
        FilePath Source,
        FilePath Target);
}

/// <summary>Levée quand une copie inter-volume échoue la vérification d'intégrité (empreintes différentes).</summary>
public sealed class ReplicationIntegrityException : Exception
{
    public ReplicationIntegrityException(FilePath source, FilePath target)
        : base($"Copie corrompue (empreintes différentes) : {source.Value} → {target.Value}")
    {
    }
}
