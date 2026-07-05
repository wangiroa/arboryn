using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Database;
using Dapper;

namespace Arboryn.Infrastructure.Persistence;

/// <summary>
/// Dépôt SQLite des périmètres de réplication (Inc 10, table <c>replication_scopes</c>).
/// L'expression est stockée en JSON via <see cref="ScopeExpressionJson"/>.
/// </summary>
public sealed class SqliteReplicationScopeRepository : IReplicationScopeRepository
{
    private readonly DatabaseFactory _databaseFactory;

    public SqliteReplicationScopeRepository(DatabaseFactory databaseFactory)
        => _databaseFactory = databaseFactory;

    public async Task<ReplicationScope?> GetAsync(ScopeId id, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<ScopeRow>(new CommandDefinition(
            "SELECT id AS Id, name AS Name, expression_json AS ExpressionJson FROM replication_scopes WHERE id = @Id;",
            new { Id = id.Value },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<ReplicationScope>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<ScopeRow>(new CommandDefinition(
            "SELECT id AS Id, name AS Name, expression_json AS ExpressionJson FROM replication_scopes ORDER BY name;",
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    public async Task UpsertAsync(ReplicationScope scope, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO replication_scopes (id, name, expression_json)
            VALUES (@Id, @Name, @ExpressionJson)
            ON CONFLICT(id) DO UPDATE SET
                name            = excluded.name,
                expression_json = excluded.expression_json;
            """;
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = scope.Id.Value,
            scope.Name,
            ExpressionJson = ScopeExpressionJson.Serialize(scope.Expression),
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task DeleteAsync(ScopeId id, CancellationToken cancellationToken)
    {
        // ON DELETE SET NULL sur volumes.replication_scope_id : les volumes rattachés
        // repassent simplement « sans périmètre » (rien en scope) sans casser de FK.
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM replication_scopes WHERE id = @Id;",
            new { Id = id.Value },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static ReplicationScope Map(ScopeRow row) => new(
        new ScopeId(row.Id),
        row.Name,
        ScopeExpressionJson.Deserialize(row.ExpressionJson));

    private sealed record ScopeRow(string Id, string Name, string ExpressionJson);
}
