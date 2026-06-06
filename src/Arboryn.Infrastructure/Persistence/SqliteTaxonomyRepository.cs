using System.Text.Json;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Taxonomy;
using Arboryn.Infrastructure.Database;
using Dapper;

namespace Arboryn.Infrastructure.Persistence;

/// <summary>
/// Dépôt SQLite de la taxonomie (table <c>library_taxonomy</c>). La version active d'une
/// catégorie l'emporte ; à défaut, la taxonomie livrée par <see cref="DefaultTaxonomies"/>.
/// Les personnalisations sont versionnées : <see cref="SaveAsync"/> désactive la version
/// précédente et en crée une nouvelle active.
/// </summary>
public sealed class SqliteTaxonomyRepository : ITaxonomyRepository
{
    private readonly DatabaseFactory _databaseFactory;

    public SqliteTaxonomyRepository(DatabaseFactory databaseFactory)
        => _databaseFactory = databaseFactory;

    public async Task<CategoryTaxonomy?> GetAsync(MediaCategory category, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT name_pattern AS NamePattern, path_pattern AS PathPattern,
                   required_fields_json AS RequiredFieldsJson, version AS Version
            FROM library_taxonomy
            WHERE category = @Category AND active = 1
            ORDER BY version DESC
            LIMIT 1;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<TaxonomyRow>(new CommandDefinition(
            sql, new { Category = LogicalFileEnums.ToDb(category) }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row is null ? DefaultTaxonomies.For(category) : Map(category, row);
    }

    public async Task<IReadOnlyList<CategoryTaxonomy>> GetAllAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT category AS Category, name_pattern AS NamePattern, path_pattern AS PathPattern,
                   required_fields_json AS RequiredFieldsJson, version AS Version
            FROM library_taxonomy t
            WHERE active = 1
              AND version = (SELECT MAX(version) FROM library_taxonomy WHERE category = t.category AND active = 1);
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<CategoryRow>(new CommandDefinition(
            sql, cancellationToken: cancellationToken)).ConfigureAwait(false);

        var result = new List<CategoryTaxonomy>();
        var covered = new HashSet<MediaCategory>();
        foreach (var row in rows)
        {
            var category = LogicalFileEnums.CategoryFromDb(row.Category);
            covered.Add(category);
            result.Add(new CategoryTaxonomy(
                category, row.PathPattern, row.NamePattern, Deserialize(row.RequiredFieldsJson), (int)row.Version));
        }

        // Complète avec les défauts des catégories non personnalisées.
        foreach (var category in Enum.GetValues<MediaCategory>())
        {
            if (covered.Contains(category))
            {
                continue;
            }

            var fallback = DefaultTaxonomies.For(category);
            if (fallback is not null)
            {
                result.Add(fallback);
            }
        }

        return result;
    }

    public async Task<int> SaveAsync(CategoryTaxonomy taxonomy, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var categoryDb = LogicalFileEnums.ToDb(taxonomy.Category);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE library_taxonomy SET active = 0, updated_at = datetime('now') WHERE category = @Category AND active = 1;",
            new { Category = categoryDb }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        var nextVersion = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COALESCE(MAX(version), 0) + 1 FROM library_taxonomy WHERE category = @Category;",
            new { Category = categoryDb }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO library_taxonomy
                (id, category, name_pattern, path_pattern, required_fields_json, active, version, created_at, updated_at)
            VALUES
                (@Id, @Category, @NamePattern, @PathPattern, @RequiredFieldsJson, 1, @Version, datetime('now'), datetime('now'));
            """,
            new
            {
                Id = Guid.NewGuid().ToString(),
                Category = categoryDb,
                NamePattern = taxonomy.NameTemplate,
                PathPattern = taxonomy.PathTemplate,
                RequiredFieldsJson = JsonSerializer.Serialize(taxonomy.RequiredFields),
                Version = nextVersion,
            },
            transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return nextVersion;
    }

    private static CategoryTaxonomy Map(MediaCategory category, TaxonomyRow row) => new(
        category, row.PathPattern, row.NamePattern, Deserialize(row.RequiredFieldsJson), (int)row.Version);

    private static IReadOnlyList<string> Deserialize(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();

    private sealed record TaxonomyRow(string NamePattern, string PathPattern, string? RequiredFieldsJson, long Version);

    private sealed record CategoryRow(string Category, string NamePattern, string PathPattern, string? RequiredFieldsJson, long Version);
}
