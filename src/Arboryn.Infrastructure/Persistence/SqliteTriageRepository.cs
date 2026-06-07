using Arboryn.Application.Abstractions;
using Arboryn.Domain.Triage;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Database;
using Dapper;

namespace Arboryn.Infrastructure.Persistence;

/// <summary>
/// Dépôt SQLite du triage : patterns d'extraction (<c>triage_patterns</c>) et corrections
/// utilisateur (<c>triage_corrections</c>). Les patterns par défaut sont semés une seule fois
/// (table vide). Les patterns appris s'y ajoutent avec une priorité élevée.
/// </summary>
public sealed class SqliteTriageRepository : ITriageRepository
{
    private readonly DatabaseFactory _databaseFactory;

    public SqliteTriageRepository(DatabaseFactory databaseFactory)
        => _databaseFactory = databaseFactory;

    public async Task<int> EnsureDefaultPatternsAsync(
        IReadOnlyList<TriagePattern> defaults, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var existing = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM triage_patterns;", cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (existing > 0)
        {
            return 0;
        }

        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var inserted = 0;
        foreach (var pattern in defaults)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                InsertPatternSql, ToParams(pattern with { Id = Guid.NewGuid().ToString() }),
                transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            inserted++;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return inserted;
    }

    public async Task<IReadOnlyList<TriagePattern>> GetActivePatternsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id AS Id, pattern_kind AS Kind, regex AS Regex, template AS Template,
                   description AS Description, learned_from_user AS LearnedFromUser,
                   priority AS Priority, active AS Active
            FROM triage_patterns
            WHERE active = 1
            ORDER BY priority DESC, created_at;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<PatternRow>(new CommandDefinition(
            sql, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(Map).ToList();
    }

    public async Task<string> AddPatternAsync(TriagePattern pattern, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString();
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            InsertPatternSql, ToParams(pattern with { Id = id }), cancellationToken: cancellationToken)).ConfigureAwait(false);
        return id;
    }

    public async Task<bool> PatternExistsAsync(
        TriagePatternKind kind, string regex, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM triage_patterns WHERE pattern_kind = @Kind AND regex = @Regex AND active = 1;",
            new { Kind = KindToDb(kind), Regex = regex }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return count > 0;
    }

    public async Task AddCorrectionAsync(
        FileInstanceId? instanceId, TriageCorrection correction, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO triage_corrections
                (id, file_instance_id, snippet, pattern_kind, extracted_value, corrected_value)
            VALUES
                (@Id, @InstanceId, @Snippet, @Kind, @Extracted, @Corrected);
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = Guid.NewGuid().ToString(),
            InstanceId = instanceId?.Value,
            Snippet = correction.Snippet,
            Kind = KindToDb(correction.Kind),
            Extracted = correction.ExtractedValue,
            Corrected = correction.CorrectedValue,
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StoredCorrection>> GetUnderivedCorrectionsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id AS Id, snippet AS Snippet, pattern_kind AS Kind,
                   extracted_value AS Extracted, corrected_value AS Corrected
            FROM triage_corrections
            WHERE derived_into_pattern_id IS NULL
            ORDER BY created_at;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<CorrectionRow>(new CommandDefinition(
            sql, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(r => new StoredCorrection(
            r.Id, new TriageCorrection(KindFromDb(r.Kind), r.Snippet, r.Extracted, r.Corrected))).ToList();
    }

    public async Task MarkCorrectionDerivedAsync(
        string correctionId, string patternId, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE triage_corrections SET derived_into_pattern_id = @PatternId WHERE id = @Id;",
            new { PatternId = patternId, Id = correctionId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private const string InsertPatternSql = """
        INSERT INTO triage_patterns
            (id, pattern_kind, regex, template, description, learned_from_user, priority, active, created_at)
        VALUES
            (@Id, @Kind, @Regex, @Template, @Description, @LearnedFromUser, @Priority, @Active, datetime('now'));
        """;

    private static object ToParams(TriagePattern p) => new
    {
        p.Id,
        Kind = KindToDb(p.Kind),
        p.Regex,
        p.Template,
        p.Description,
        LearnedFromUser = p.LearnedFromUser ? 1 : 0,
        p.Priority,
        Active = p.Active ? 1 : 0,
    };

    private static TriagePattern Map(PatternRow r) => new(
        r.Id, KindFromDb(r.Kind), r.Regex, r.Template, r.Description,
        r.LearnedFromUser != 0, (int)r.Priority, r.Active != 0);

    private static string KindToDb(TriagePatternKind kind) => kind switch
    {
        TriagePatternKind.Source => "source",
        TriagePatternKind.Object => "object",
        TriagePatternKind.Date => "date",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static TriagePatternKind KindFromDb(string value) => value switch
    {
        "source" => TriagePatternKind.Source,
        "object" => TriagePatternKind.Object,
        "date" => TriagePatternKind.Date,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    private sealed record PatternRow(
        string Id, string Kind, string Regex, string? Template, string? Description,
        long LearnedFromUser, long Priority, long Active);

    private sealed record CorrectionRow(string Id, string Snippet, string Kind, string? Extracted, string Corrected);
}
