using System.Globalization;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Database;
using Dapper;

namespace Arboryn.Infrastructure.Persistence;

/// <summary>Stockage SQLite des métadonnées (table <c>file_metadata</c>).</summary>
public sealed class SqliteFileMetadataRepository : IFileMetadataRepository
{
    private readonly DatabaseFactory _databaseFactory;

    public SqliteFileMetadataRepository(DatabaseFactory databaseFactory)
        => _databaseFactory = databaseFactory;

    public async Task UpsertAsync(MetadataEntry entry, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO file_metadata (file_instance_id, key, value, source, confidence, extracted_at)
            VALUES (@InstanceId, @Key, @Value, @Source, @Confidence, @ExtractedAt)
            ON CONFLICT(file_instance_id, key, source) DO UPDATE SET
                value = excluded.value,
                confidence = excluded.confidence,
                extracted_at = excluded.extracted_at;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            InstanceId = entry.InstanceId.Value,
            entry.Key,
            entry.Value,
            entry.Source,
            entry.Confidence,
            ExtractedAt = entry.ExtractedAt.ToString("O", CultureInfo.InvariantCulture),
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MetadataEntry>> GetForInstanceAsync(
        FileInstanceId instanceId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT file_instance_id AS InstanceId,
                   key              AS Key,
                   value            AS Value,
                   source           AS Source,
                   confidence       AS Confidence,
                   extracted_at     AS ExtractedAt
            FROM file_metadata
            WHERE file_instance_id = @InstanceId
            ORDER BY key, confidence DESC;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<EntryRow>(new CommandDefinition(
            sql, new { InstanceId = instanceId.Value }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyDictionary<string, MetadataEntry>> GetFusedAsync(
        FileInstanceId instanceId, CancellationToken cancellationToken)
    {
        var all = await GetForInstanceAsync(instanceId, cancellationToken).ConfigureAwait(false);

        return all
            .GroupBy(e => e.Key, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(e => e.Confidence)
                      .ThenByDescending(e => e.ExtractedAt)
                      .First(),
                StringComparer.Ordinal);
    }

    public async Task DeleteForInstanceAsync(FileInstanceId instanceId, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM file_metadata WHERE file_instance_id = @InstanceId;",
            new { InstanceId = instanceId.Value },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static MetadataEntry Map(EntryRow r) => new(
        new FileInstanceId(r.InstanceId),
        r.Key,
        r.Value,
        r.Source,
        r.Confidence,
        DateTime.Parse(r.ExtractedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    private sealed record EntryRow(
        string InstanceId,
        string Key,
        string? Value,
        string Source,
        double Confidence,
        string ExtractedAt);
}
