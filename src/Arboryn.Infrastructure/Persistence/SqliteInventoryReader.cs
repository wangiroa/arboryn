using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Database;
using Dapper;

namespace Arboryn.Infrastructure.Persistence;

/// <summary>
/// Lecture SQLite agrégée pour le tableau de bord inventaire (Inc 11). S'appuie sur les index
/// existants (<c>file_instances(volume_id)</c>, <c>(status)</c>, <c>logical_files(category)</c>,
/// <c>file_instances(canonical_name)</c>) pour rester rapide à grande échelle.
/// </summary>
public sealed class SqliteInventoryReader : IInventoryReader
{
    private readonly DatabaseFactory _databaseFactory;

    public SqliteInventoryReader(DatabaseFactory databaseFactory)
        => _databaseFactory = databaseFactory;

    public async Task<IReadOnlyList<VolumeCategoryPresence>> GetPresenceAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT fi.volume_id AS VolumeId, lf.category AS Category,
                   COUNT(*) AS Count, COALESCE(SUM(fi.size), 0) AS SpaceBytes
            FROM file_instances fi
            JOIN logical_files lf ON lf.id = fi.logical_file_id
            WHERE fi.status = 'active'
            GROUP BY fi.volume_id, lf.category;
            """;
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<PresenceRow>(new CommandDefinition(
            sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows
            .Select(r => new VolumeCategoryPresence(
                new VolumeId(r.VolumeId), LogicalFileEnums.CategoryFromDb(r.Category), (int)r.Count, r.SpaceBytes))
            .ToList();
    }

    public async Task<IReadOnlyList<CategoryTotal>> GetCategoryTotalsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT lf.category AS Category,
                   COUNT(DISTINCT lf.id) AS LogicalFiles,
                   COALESCE(SUM(fi.size), 0) AS SpaceBytes
            FROM logical_files lf
            JOIN file_instances fi ON fi.logical_file_id = lf.id
            WHERE fi.status = 'active'
            GROUP BY lf.category;
            """;
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<CategoryTotalRow>(new CommandDefinition(
            sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows
            .Select(r => new CategoryTotal(LogicalFileEnums.CategoryFromDb(r.Category), (int)r.LogicalFiles, r.SpaceBytes))
            .ToList();
    }

    public async Task<GlobalInventoryCounts> GetGlobalCountsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                (SELECT COUNT(DISTINCT logical_file_id) FROM file_instances WHERE status = 'active' AND logical_file_id IS NOT NULL) AS LogicalFiles,
                (SELECT COUNT(*) FROM file_instances WHERE status = 'active') AS FileInstances,
                (SELECT COALESCE(SUM(size), 0) FROM file_instances WHERE status = 'active') AS TotalSpaceBytes;
            """;
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleAsync<GlobalRow>(new CommandDefinition(
            sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return new GlobalInventoryCounts(row.LogicalFiles, row.FileInstances, row.TotalSpaceBytes);
    }

    public async Task<IReadOnlyList<CrossVolumeSearchHit>> SearchAsync(
        string query, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return System.Array.Empty<CrossVolumeSearchHit>();
        }

        const string sql = """
            SELECT fi.logical_file_id AS LogicalFileId, lf.category AS Category,
                   fi.relative_path AS RelativePath, v.name AS VolumeName
            FROM file_instances fi
            JOIN logical_files lf ON lf.id = fi.logical_file_id
            JOIN volumes v ON v.id = fi.volume_id
            WHERE fi.status = 'active'
              AND (fi.canonical_name LIKE @Pattern ESCAPE '\' OR fi.relative_path LIKE @Pattern ESCAPE '\')
            ORDER BY fi.logical_file_id, v.name
            LIMIT @Limit;
            """;
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<SearchRow>(new CommandDefinition(
            sql, new { Pattern = "%" + Escape(query.Trim()) + "%", Limit = limit }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows
            .Where(r => r.LogicalFileId is not null)
            .Select(r => new CrossVolumeSearchHit(
                new LogicalFileId(r.LogicalFileId!), LogicalFileEnums.CategoryFromDb(r.Category), r.RelativePath, r.VolumeName))
            .ToList();
    }

    public async Task<IReadOnlyList<InventoryWorkItem>> GetMissingAsync(
        VolumeId volumeId, IReadOnlyList<Domain.Enums.MediaCategory> inScopeCategories, int limit, CancellationToken cancellationToken)
    {
        var dbCategories = inScopeCategories.Select(LogicalFileEnums.ToDb).ToList();
        if (dbCategories.Count == 0)
        {
            return System.Array.Empty<InventoryWorkItem>();
        }

        const string sql = """
            SELECT lf.category AS Category,
                   (SELECT fi2.relative_path FROM file_instances fi2
                    WHERE fi2.logical_file_id = lf.id AND fi2.status = 'active' LIMIT 1) AS SamplePath
            FROM logical_files lf
            WHERE lf.category IN @Categories
              AND EXISTS (SELECT 1 FROM file_instances fx WHERE fx.logical_file_id = lf.id AND fx.status = 'active')
              AND NOT EXISTS (SELECT 1 FROM file_instances fi
                              WHERE fi.logical_file_id = lf.id AND fi.volume_id = @VolumeId AND fi.status = 'active')
            LIMIT @Limit;
            """;
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<WorkRow>(new CommandDefinition(
            sql, new { Categories = dbCategories, VolumeId = volumeId.Value, Limit = limit },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(ToWork).ToList();
    }

    public async Task<IReadOnlyList<InventoryWorkItem>> GetSurplusAsync(
        VolumeId volumeId, IReadOnlyList<Domain.Enums.MediaCategory> inScopeCategories, int limit, CancellationToken cancellationToken)
    {
        var dbCategories = inScopeCategories.Select(LogicalFileEnums.ToDb).ToList();
        if (dbCategories.Count == 0)
        {
            return System.Array.Empty<InventoryWorkItem>();
        }

        const string sql = """
            SELECT lf.category AS Category, fi.relative_path AS SamplePath
            FROM file_instances fi
            JOIN logical_files lf ON lf.id = fi.logical_file_id
            WHERE fi.volume_id = @VolumeId AND fi.status = 'active' AND lf.category NOT IN @Categories
            LIMIT @Limit;
            """;
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<WorkRow>(new CommandDefinition(
            sql, new { Categories = dbCategories, VolumeId = volumeId.Value, Limit = limit },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(ToWork).ToList();
    }

    private static InventoryWorkItem ToWork(WorkRow r) => new(
        LogicalFileEnums.CategoryFromDb(r.Category),
        string.IsNullOrEmpty(r.SamplePath) ? "(sans nom)" : System.IO.Path.GetFileName(r.SamplePath));

    /// <summary>Échappe les jokers LIKE (<c>%</c>, <c>_</c>, <c>\</c>) pour une recherche littérale.</summary>
    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private sealed record PresenceRow(string VolumeId, string Category, long Count, long SpaceBytes);

    private sealed record CategoryTotalRow(string Category, long LogicalFiles, long SpaceBytes);

    private sealed record GlobalRow(long LogicalFiles, long FileInstances, long TotalSpaceBytes);

    private sealed record SearchRow(string? LogicalFileId, string Category, string RelativePath, string VolumeName);

    private sealed record WorkRow(string Category, string? SamplePath);
}
