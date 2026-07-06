using System.Globalization;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Database;
using Dapper;

namespace Arboryn.Infrastructure.Persistence;

/// <summary>Journal d'opérations sur SQLite (table <c>operations</c>).</summary>
public sealed class SqliteOperationJournal : IOperationJournal
{
    private readonly DatabaseFactory _databaseFactory;

    public SqliteOperationJournal(DatabaseFactory databaseFactory)
        => _databaseFactory = databaseFactory;

    public async Task AppendAsync(Operation operation, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO operations
                (id, kind, file_instance_id, source_volume_id, target_volume_id, old_path, new_path,
                 old_metadata_json, status, batch_id, executed_at, created_at)
            VALUES
                (@Id, @Kind, @FileInstanceId, @SourceVolumeId, @TargetVolumeId, @OldPath, @NewPath,
                 @OldMetadataJson, @Status, @BatchId, @ExecutedAt, @CreatedAt);
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = operation.Id.Value,
            Kind = OperationEnums.ToDb(operation.Kind),
            FileInstanceId = operation.FileInstanceId.Value,
            SourceVolumeId = operation.SourceVolumeId?.Value,
            TargetVolumeId = operation.TargetVolumeId?.Value,
            OldPath = operation.OldPath?.Value,
            NewPath = operation.NewPath?.Value,
            OldMetadataJson = operation.OldMetadataJson,
            Status = OperationEnums.ToDb(operation.Status),
            BatchId = operation.BatchId.Value,
            ExecutedAt = ToIso(operation.ExecutedAt),
            CreatedAt = ToIso(operation.CreatedAt)!,
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<BatchId?> GetLastUndoableDeleteBatchAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT batch_id
            FROM operations
            WHERE kind = 'delete' AND status = 'completed' AND batch_id IS NOT NULL
            GROUP BY batch_id
            ORDER BY MAX(COALESCE(executed_at, created_at)) DESC
            LIMIT 1;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var batchId = await connection.ExecuteScalarAsync<string?>(
            new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return batchId is null ? null : new BatchId(batchId);
    }

    public async Task<BatchId?> GetLastUndoableUniformizationBatchAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT batch_id
            FROM operations
            WHERE kind IN ('move', 'rename') AND status = 'completed' AND batch_id IS NOT NULL
            GROUP BY batch_id
            ORDER BY MAX(COALESCE(executed_at, created_at)) DESC
            LIMIT 1;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var batchId = await connection.ExecuteScalarAsync<string?>(
            new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return batchId is null ? null : new BatchId(batchId);
    }

    public async Task<BatchId?> GetLastUndoableWriteBackBatchAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT batch_id
            FROM operations
            WHERE kind = 'metadata_writeback' AND status = 'completed' AND batch_id IS NOT NULL
            GROUP BY batch_id
            ORDER BY MAX(COALESCE(executed_at, created_at)) DESC
            LIMIT 1;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var batchId = await connection.ExecuteScalarAsync<string?>(
            new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return batchId is null ? null : new BatchId(batchId);
    }

    private const string SelectColumns = """
        SELECT id                AS Id,
               batch_id          AS BatchId,
               kind              AS Kind,
               file_instance_id  AS FileInstanceId,
               source_volume_id  AS SourceVolumeId,
               target_volume_id  AS TargetVolumeId,
               old_path          AS OldPath,
               new_path          AS NewPath,
               old_metadata_json AS OldMetadataJson,
               status            AS Status,
               created_at        AS CreatedAt,
               executed_at       AS ExecutedAt,
               undone_at         AS UndoneAt
        FROM operations
        """;

    public async Task<IReadOnlyList<Operation>> GetBatchAsync(BatchId batchId, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<OpRow>(new CommandDefinition(
            $"{SelectColumns} WHERE batch_id = @BatchId ORDER BY created_at;",
            new { BatchId = batchId.Value }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<Operation>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<OpRow>(new CommandDefinition(
            $"{SelectColumns} ORDER BY COALESCE(executed_at, created_at) DESC LIMIT @Limit;",
            new { Limit = limit }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<Operation>> GetPendingReplicationOperationsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<OpRow>(new CommandDefinition(
            $"{SelectColumns} WHERE status = 'pending' AND source_volume_id IS NOT NULL ORDER BY created_at;",
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(Map).ToList();
    }

    public async Task MarkUndoneAsync(OperationId operationId, DateTime undoneAt, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE operations SET status = 'undone', undone_at = @UndoneAt WHERE id = @Id;",
            new { UndoneAt = ToIso(undoneAt)!, Id = operationId.Value },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task SetStatusAsync(
        OperationId operationId, OperationStatus status, DateTime? executedAt, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE operations SET status = @Status, executed_at = COALESCE(@ExecutedAt, executed_at) WHERE id = @Id;",
            new { Status = OperationEnums.ToDb(status), ExecutedAt = ToIso(executedAt), Id = operationId.Value },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static Operation Map(OpRow r) => new(
        new OperationId(r.Id),
        new BatchId(r.BatchId),
        OperationEnums.KindFromDb(r.Kind),
        new FileInstanceId(r.FileInstanceId ?? string.Empty),
        r.OldPath is null ? null : FilePath.From(r.OldPath),
        r.NewPath is null ? null : FilePath.From(r.NewPath),
        OperationEnums.StatusFromDb(r.Status),
        ParseIso(r.CreatedAt)!.Value,
        ParseIso(r.ExecutedAt),
        ParseIso(r.UndoneAt),
        r.OldMetadataJson,
        r.SourceVolumeId is null ? null : new VolumeId(r.SourceVolumeId),
        r.TargetVolumeId is null ? null : new VolumeId(r.TargetVolumeId));

    private static string? ToIso(DateTime? value)
        => value?.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime? ParseIso(string? value)
        => value is null ? null : DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record OpRow(
        string Id,
        string BatchId,
        string Kind,
        string? FileInstanceId,
        string? SourceVolumeId,
        string? TargetVolumeId,
        string? OldPath,
        string? NewPath,
        string? OldMetadataJson,
        string Status,
        string CreatedAt,
        string? ExecutedAt,
        string? UndoneAt);
}

/// <summary>Conversion entre les enums du domaine et les valeurs texte de la base.</summary>
internal static class OperationEnums
{
    public static string ToDb(OperationKind kind) => kind switch
    {
        OperationKind.Rename => "rename",
        OperationKind.Move => "move",
        OperationKind.Copy => "copy",
        OperationKind.Delete => "delete",
        OperationKind.MetadataWriteback => "metadata_writeback",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static OperationKind KindFromDb(string value) => value switch
    {
        "rename" => OperationKind.Rename,
        "move" => OperationKind.Move,
        "copy" => OperationKind.Copy,
        "delete" => OperationKind.Delete,
        "metadata_writeback" => OperationKind.MetadataWriteback,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string ToDb(OperationStatus status) => status switch
    {
        OperationStatus.Pending => "pending",
        OperationStatus.InProgress => "in_progress",
        OperationStatus.Completed => "completed",
        OperationStatus.Failed => "failed",
        OperationStatus.Cancelled => "cancelled",
        OperationStatus.Undone => "undone",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static OperationStatus StatusFromDb(string value) => value switch
    {
        "pending" => OperationStatus.Pending,
        "in_progress" => OperationStatus.InProgress,
        "completed" => OperationStatus.Completed,
        "failed" => OperationStatus.Failed,
        "cancelled" => OperationStatus.Cancelled,
        "undone" => OperationStatus.Undone,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}
