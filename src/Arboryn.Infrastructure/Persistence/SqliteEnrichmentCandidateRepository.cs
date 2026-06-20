using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Database;
using Dapper;

namespace Arboryn.Infrastructure.Persistence;

/// <summary>
/// Stockage SQLite des candidats d'enrichissement (table <c>enrichment_candidates</c>).
/// L'UPSERT ne ressuscite pas un candidat décidé tant que la valeur proposée est inchangée.
/// </summary>
public sealed class SqliteEnrichmentCandidateRepository : IEnrichmentCandidateRepository
{
    private readonly DatabaseFactory _databaseFactory;

    public SqliteEnrichmentCandidateRepository(DatabaseFactory databaseFactory)
        => _databaseFactory = databaseFactory;

    public async Task UpsertAsync(EnrichmentCandidateRecord candidate, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO enrichment_candidates (id, file_instance_id, provider, key, value, confidence, status)
            VALUES (@Id, @InstanceId, @Provider, @Key, @Value, @Confidence, @Status)
            ON CONFLICT(file_instance_id, provider, key) DO UPDATE SET
                value      = excluded.value,
                confidence = excluded.confidence,
                status     = CASE WHEN enrichment_candidates.value <> excluded.value
                                  THEN 'pending' ELSE enrichment_candidates.status END,
                decided_at = CASE WHEN enrichment_candidates.value <> excluded.value
                                  THEN NULL ELSE enrichment_candidates.decided_at END,
                created_at = datetime('now');
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            candidate.Id,
            InstanceId = candidate.InstanceId.Value,
            candidate.Provider,
            candidate.Key,
            candidate.Value,
            candidate.Confidence,
            Status = ToText(candidate.Status),
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PendingEnrichmentCandidate>> GetPendingAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.id               AS Id,
                   c.file_instance_id AS InstanceId,
                   fi.relative_path   AS Path,
                   c.provider         AS Provider,
                   c.key              AS Key,
                   c.value            AS Value,
                   c.confidence       AS Confidence
            FROM enrichment_candidates c
            JOIN file_instances fi ON fi.id = c.file_instance_id
            WHERE c.status = 'pending'
            ORDER BY c.confidence DESC, fi.relative_path, c.key;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<PendingRow>(new CommandDefinition(
            sql, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(r => new PendingEnrichmentCandidate(
            r.Id, new FileInstanceId(r.InstanceId), r.Path, r.Provider, r.Key, r.Value, r.Confidence)).ToList();
    }

    public async Task<EnrichmentCandidateRecord?> GetAsync(string candidateId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id               AS Id,
                   file_instance_id AS InstanceId,
                   provider         AS Provider,
                   key              AS Key,
                   value            AS Value,
                   confidence       AS Confidence,
                   status           AS Status
            FROM enrichment_candidates
            WHERE id = @Id;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<CandidateRow>(new CommandDefinition(
            sql, new { Id = candidateId }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row is null
            ? null
            : new EnrichmentCandidateRecord(
                row.Id, new FileInstanceId(row.InstanceId), row.Provider, row.Key, row.Value,
                row.Confidence, FromText(row.Status));
    }

    public async Task SetStatusAsync(
        string candidateId, EnrichmentCandidateStatus status, CancellationToken cancellationToken)
    {
        // Horodate la décision quand elle est définitive ; l'efface si on repasse en attente.
        const string sql = """
            UPDATE enrichment_candidates
            SET status = @Status,
                decided_at = CASE WHEN @Status = 'pending' THEN NULL ELSE datetime('now') END
            WHERE id = @Id;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { Id = candidateId, Status = ToText(status) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<int> CountPendingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM enrichment_candidates WHERE status = 'pending';",
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static string ToText(EnrichmentCandidateStatus status) => status switch
    {
        EnrichmentCandidateStatus.Accepted => "accepted",
        EnrichmentCandidateStatus.Rejected => "rejected",
        _ => "pending",
    };

    private static EnrichmentCandidateStatus FromText(string status) => status switch
    {
        "accepted" => EnrichmentCandidateStatus.Accepted,
        "rejected" => EnrichmentCandidateStatus.Rejected,
        _ => EnrichmentCandidateStatus.Pending,
    };

    private sealed record PendingRow(
        string Id, string InstanceId, string Path, string Provider, string Key, string Value, double Confidence);

    private sealed record CandidateRow(
        string Id, string InstanceId, string Provider, string Key, string Value, double Confidence, string Status);
}
