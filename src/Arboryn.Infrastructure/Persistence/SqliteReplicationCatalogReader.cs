using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Database;
using Dapper;

namespace Arboryn.Infrastructure.Persistence;

/// <summary>
/// Lecture SQLite du catalogue logique pour la réplication (Inc 10) : joint chaque
/// <c>logical_file</c> à ses <c>file_instances</c> actives et regroupe le tout par œuvre.
/// Les œuvres sans instance active sont naturellement absentes (jointure interne).
/// </summary>
public sealed class SqliteReplicationCatalogReader : IReplicationCatalogReader
{
    private readonly DatabaseFactory _databaseFactory;

    public SqliteReplicationCatalogReader(DatabaseFactory databaseFactory)
        => _databaseFactory = databaseFactory;

    public async Task<IReadOnlyList<LogicalFileInstances>> GetAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT lf.id            AS LogicalId,
                   lf.category      AS Category,
                   fi.id            AS InstanceId,
                   fi.volume_id     AS VolumeId,
                   fi.relative_path AS RelativePath,
                   fi.size          AS Size
            FROM logical_files lf
            JOIN file_instances fi ON fi.logical_file_id = lf.id
            WHERE fi.status = 'active'
            ORDER BY lf.id;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            sql, cancellationToken: cancellationToken)).ConfigureAwait(false);

        var result = new List<LogicalFileInstances>();
        foreach (var group in rows.GroupBy(r => r.LogicalId))
        {
            var first = group.First();
            var instances = group
                .Select(r => new CatalogInstanceRow(
                    new FileInstanceId(r.InstanceId),
                    new VolumeId(r.VolumeId),
                    r.RelativePath,
                    r.Size))
                .ToList();

            result.Add(new LogicalFileInstances(
                new LogicalFileId(group.Key),
                LogicalFileEnums.CategoryFromDb(first.Category),
                instances));
        }

        return result;
    }

    private sealed record Row(
        string LogicalId,
        string Category,
        string InstanceId,
        string VolumeId,
        string RelativePath,
        long Size);
}
