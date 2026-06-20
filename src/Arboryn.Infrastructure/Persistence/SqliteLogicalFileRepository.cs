using System.Globalization;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Database;
using Dapper;

namespace Arboryn.Infrastructure.Persistence;

/// <summary>Dépôt SQLite des <see cref="LogicalFile"/> (table <c>logical_files</c>).</summary>
public sealed class SqliteLogicalFileRepository : ILogicalFileRepository
{
    private readonly DatabaseFactory _databaseFactory;

    public SqliteLogicalFileRepository(DatabaseFactory databaseFactory)
        => _databaseFactory = databaseFactory;

    public async Task<LogicalFile?> FindBySignatureAsync(
        ContentSignature signature, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id                     AS Id,
                   category               AS Category,
                   content_signature_kind AS Kind,
                   content_signature      AS Signature,
                   canonical_path         AS CanonicalPath,
                   canonical_filename     AS CanonicalFilename,
                   created_at             AS CreatedAt,
                   updated_at             AS UpdatedAt
            FROM logical_files
            WHERE content_signature_kind = @Kind AND content_signature = @Value
            LIMIT 1;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<LogicalFileRow>(new CommandDefinition(
            sql,
            new { Kind = LogicalFileEnums.ToDb(signature.Kind), Value = signature.Value },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row is null ? null : Map(row);
    }

    public async Task UpsertAsync(LogicalFile logicalFile, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO logical_files
                (id, category, content_signature_kind, content_signature, canonical_path, canonical_filename, created_at, updated_at)
            VALUES
                (@Id, @Category, @Kind, @Signature, @CanonicalPath, @CanonicalFilename, @CreatedAt, @UpdatedAt)
            ON CONFLICT(id) DO UPDATE SET
                category               = excluded.category,
                content_signature_kind = excluded.content_signature_kind,
                content_signature      = excluded.content_signature,
                canonical_path         = excluded.canonical_path,
                canonical_filename     = excluded.canonical_filename,
                updated_at             = excluded.updated_at;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = logicalFile.Id.Value,
            Category = LogicalFileEnums.ToDb(logicalFile.Category),
            Kind = LogicalFileEnums.ToDb(logicalFile.Signature.Kind),
            Signature = logicalFile.Signature.Value,
            logicalFile.CanonicalPath,
            logicalFile.CanonicalFilename,
            CreatedAt = ToIso(logicalFile.CreatedAt),
            UpdatedAt = ToIso(logicalFile.UpdatedAt),
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task UpdateCategoryAsync(
        LogicalFileId id, MediaCategory category, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE logical_files
            SET category = @Category, updated_at = datetime('now')
            WHERE id = @Id;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id.Value, Category = LogicalFileEnums.ToDb(category) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<int> SetCategoryByInstanceAsync(
        FileInstanceId instanceId, MediaCategory category, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE logical_files
            SET category = @Category, updated_at = datetime('now')
            WHERE id = (SELECT logical_file_id FROM file_instances WHERE id = @InstanceId);
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { InstanceId = instanceId.Value, Category = LogicalFileEnums.ToDb(category) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task BackfillUnattachedAsync(CancellationToken cancellationToken)
    {
        // 1) Crée les LogicalFiles manquants pour chaque signature « name_size »
        //    portée par au moins une FileInstance orpheline.
        // 2) Rattache toutes les FileInstances orphelines au LF correspondant.
        // Les ids générés ici prennent 32 caractères hex (hex(randomblob(16))) — format
        // valide pour la colonne TEXT, sans tirets contrairement aux Guid.NewGuid().
        const string createMissing = """
            INSERT INTO logical_files (id, category, content_signature_kind, content_signature, created_at, updated_at)
            SELECT lower(hex(randomblob(16))), 'unknown', 'name_size',
                   fi.canonical_name || '|' || fi.size, datetime('now'), datetime('now')
            FROM file_instances fi
            WHERE fi.logical_file_id IS NULL
              AND NOT EXISTS (
                  SELECT 1 FROM logical_files lf
                  WHERE lf.content_signature_kind = 'name_size'
                    AND lf.content_signature = fi.canonical_name || '|' || fi.size
              )
            GROUP BY fi.canonical_name, fi.size;
            """;

        const string attach = """
            UPDATE file_instances
            SET logical_file_id = (
                SELECT id FROM logical_files
                WHERE content_signature_kind = 'name_size'
                  AND content_signature = file_instances.canonical_name || '|' || file_instances.size
            )
            WHERE logical_file_id IS NULL;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(createMissing, transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(attach, transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CatalogMetrics> GetMetricsAsync(VolumeId volumeId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*) AS Instances,
                   COUNT(DISTINCT logical_file_id) AS LogicalFiles
            FROM file_instances
            WHERE volume_id = @VolumeId AND status = 'active';
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleAsync<(long Instances, long LogicalFiles)>(new CommandDefinition(
            sql, new { VolumeId = volumeId.Value }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return new CatalogMetrics(row.LogicalFiles, row.Instances);
    }

    public async Task<IReadOnlyList<LogicalFileSummary>> GetSummariesAsync(
        CatalogFilter filter, CancellationToken cancellationToken)
    {
        // Filtrage au niveau instance puis agrégation par LogicalFile. Un LogicalFile
        // est retenu dès qu'au moins une de ses instances satisfait les critères.
        var clauses = new List<string> { "fi.status = 'active'" };
        var parameters = new DynamicParameters();

        if (filter.Category is { } category)
        {
            clauses.Add("lf.category = @Category");
            parameters.Add("Category", LogicalFileEnums.ToDb(category));
        }

        if (!string.IsNullOrWhiteSpace(filter.VolumeId))
        {
            clauses.Add("fi.volume_id = @VolumeId");
            parameters.Add("VolumeId", filter.VolumeId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Directory))
        {
            // Sous-arbre du dossier choisi : on ajoute un séparateur final pour ne pas
            // capturer un dossier voisin de même préfixe (« C:\Photos » vs « C:\Photos2 »).
            clauses.Add("fi.relative_path LIKE @DirPrefix ESCAPE '\\'");
            parameters.Add("DirPrefix", LikeDirPrefix(filter.Directory!));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // Recherche libre sur nom, chemin et valeurs de métadonnées.
            clauses.Add("""
                (fi.canonical_name LIKE @Search ESCAPE '\'
                 OR fi.relative_path LIKE @Search ESCAPE '\'
                 OR EXISTS (SELECT 1 FROM file_metadata m
                            WHERE m.file_instance_id = fi.id AND m.value LIKE @Search ESCAPE '\'))
                """);
            parameters.Add("Search", LikeContains(filter.Search!));
        }

        var where = string.Join(" AND ", clauses);
        // CAST explicite des colonnes calculées : sans type déclaré, Microsoft.Data.Sqlite
        // renvoie byte[] par défaut pour ces colonnes, ce qui casse la matérialisation Dapper
        // du record (constructeur positionnel typé long/string).
        var sql = $"""
            SELECT CAST(lf.id AS TEXT)                     AS Id,
                   CAST(lf.category AS TEXT)               AS Category,
                   CAST(lf.content_signature_kind AS TEXT) AS Kind,
                   CAST(lf.content_signature AS TEXT)      AS Signature,
                   CAST(COUNT(fi.id) AS INTEGER)           AS InstanceCount,
                   CAST(COALESCE(SUM(fi.size), 0) AS INTEGER) AS TotalSize,
                   CAST(COALESCE(MAX(fi.size), 0) AS INTEGER) AS MaxSize,
                   CAST(MIN(fi.id) AS TEXT)                AS SampleInstanceId,
                   CAST(MIN(fi.relative_path) AS TEXT)     AS SampleRelativePath,
                   CAST(MIN(fi.volume_id) AS TEXT)         AS SampleVolumeId
            FROM logical_files lf
            JOIN file_instances fi ON fi.logical_file_id = lf.id
            WHERE {where}
            GROUP BY lf.id, lf.category, lf.content_signature_kind, lf.content_signature
            ORDER BY (COALESCE(SUM(fi.size), 0) - COALESCE(MAX(fi.size), 0)) DESC, COUNT(fi.id) DESC;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var volumeNames = (await connection.QueryAsync<(string Id, string Name)>(new CommandDefinition(
            "SELECT id, name FROM volumes;", cancellationToken: cancellationToken)).ConfigureAwait(false))
            .ToDictionary(v => v.Id, v => v.Name, StringComparer.Ordinal);

        // Lecture en lignes dynamiques plutôt qu'en record positionnel : pour les colonnes
        // calculées (COUNT/SUM/MIN…) sans type déclaré, Microsoft.Data.Sqlite expose byte[]
        // via GetFieldType, ce qui fait échouer la matérialisation par constructeur de Dapper.
        // GetValue, lui, renvoie la valeur réelle (long/string) — on mappe donc à la main.
        var rows = await connection.QueryAsync(new CommandDefinition(
            sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

        var result = new List<LogicalFileSummary>();
        foreach (var row in rows)
        {
            var r = (IDictionary<string, object>)row;
            var volumeId = AsString(r["SampleVolumeId"]);
            result.Add(new LogicalFileSummary(
                new LogicalFileId(AsString(r["Id"])),
                new ContentSignature(LogicalFileEnums.KindFromDb(AsString(r["Kind"])), AsString(r["Signature"])),
                (int)AsLong(r["InstanceCount"]),
                AsLong(r["TotalSize"]),
                AsLong(r["MaxSize"]),
                new FileInstanceId(AsString(r["SampleInstanceId"])),
                LogicalFileEnums.CategoryFromDb(AsString(r["Category"])),
                DirectoryOf(AsString(r["SampleRelativePath"])),
                volumeNames.TryGetValue(volumeId, out var name) ? name : volumeId));
        }

        return result;
    }

    private static string AsString(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        byte[] b => System.Text.Encoding.UTF8.GetString(b),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static long AsLong(object? value) => value switch
    {
        null => 0L,
        long l => l,
        int i => i,
        _ => Convert.ToInt64(value, CultureInfo.InvariantCulture),
    };

    public async Task<CatalogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var categories = (await connection.QueryAsync<string>(new CommandDefinition("""
            SELECT DISTINCT lf.category
            FROM logical_files lf
            JOIN file_instances fi ON fi.logical_file_id = lf.id
            WHERE fi.status = 'active';
            """, cancellationToken: cancellationToken)).ConfigureAwait(false))
            .Select(LogicalFileEnums.CategoryFromDb)
            .Distinct()
            .OrderBy(c => c.ToString(), StringComparer.Ordinal)
            .ToList();

        var volumes = (await connection.QueryAsync<(string Id, string Name)>(new CommandDefinition("""
            SELECT DISTINCT v.id, v.name
            FROM volumes v
            JOIN file_instances fi ON fi.volume_id = v.id
            WHERE fi.status = 'active'
            ORDER BY v.name;
            """, cancellationToken: cancellationToken)).ConfigureAwait(false))
            .Select(v => new VolumeOption(v.Id, v.Name))
            .ToList();

        return new CatalogFilterOptions(categories, volumes);
    }

    /// <summary>Répertoire parent d'un chemin (absolu jusqu'à l'Inc 9). Vide si introuvable.</summary>
    private static string DirectoryOf(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var separator = path.LastIndexOfAny(new[] { '\\', '/' });
        return separator <= 0 ? string.Empty : path[..separator];
    }

    /// <summary>Échappe les jokers LIKE (% _ \) d'une valeur littérale.</summary>
    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string LikeContains(string value) => "%" + EscapeLike(value.Trim()) + "%";

    /// <summary>Motif LIKE pour le sous-arbre d'un dossier (séparateur final garanti).</summary>
    private static string LikeDirPrefix(string directory)
        => EscapeLike(directory.Trim().TrimEnd('\\', '/') + "\\") + "%";


    public async Task DeleteOrphansAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM logical_files
            WHERE id NOT IN (
                SELECT DISTINCT logical_file_id FROM file_instances
                WHERE logical_file_id IS NOT NULL
            );
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static LogicalFile Map(LogicalFileRow r) => new(
        new LogicalFileId(r.Id),
        LogicalFileEnums.CategoryFromDb(r.Category),
        new ContentSignature(LogicalFileEnums.KindFromDb(r.Kind), r.Signature),
        DateTime.Parse(r.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        DateTime.Parse(r.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        r.CanonicalPath,
        r.CanonicalFilename);

    private static string ToIso(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    private sealed record LogicalFileRow(
        string Id,
        string Category,
        string Kind,
        string Signature,
        string? CanonicalPath,
        string? CanonicalFilename,
        string CreatedAt,
        string UpdatedAt);
}

/// <summary>Conversion entre les enums du domaine et les valeurs texte de la base.</summary>
internal static class LogicalFileEnums
{
    public static string ToDb(ContentSignatureKind kind) => kind switch
    {
        ContentSignatureKind.NameSize => "name_size",
        ContentSignatureKind.Sha256 => "sha256",
        ContentSignatureKind.PHash => "phash",
        ContentSignatureKind.Chromaprint => "chromaprint",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static ContentSignatureKind KindFromDb(string value) => value switch
    {
        "name_size" => ContentSignatureKind.NameSize,
        "sha256" => ContentSignatureKind.Sha256,
        "phash" => ContentSignatureKind.PHash,
        "chromaprint" => ContentSignatureKind.Chromaprint,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string ToDb(MediaCategory category) => category switch
    {
        MediaCategory.Unknown => "unknown",
        MediaCategory.Audiobook => "audiobook",
        MediaCategory.Book => "book",
        MediaCategory.Video => "video",
        MediaCategory.Photo => "photo",
        MediaCategory.OfficialDocument => "official_document",
        MediaCategory.OtherDocument => "other_document",
        _ => "unknown",
    };

    public static MediaCategory CategoryFromDb(string value) => value switch
    {
        "audiobook" => MediaCategory.Audiobook,
        "book" => MediaCategory.Book,
        "video" => MediaCategory.Video,
        "photo" => MediaCategory.Photo,
        "official_document" => MediaCategory.OfficialDocument,
        "other_document" => MediaCategory.OtherDocument,
        _ => MediaCategory.Unknown,
    };
}
