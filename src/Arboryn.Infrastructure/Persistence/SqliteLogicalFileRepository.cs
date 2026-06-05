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
        VolumeId volumeId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT lf.id                     AS Id,
                   lf.content_signature_kind AS Kind,
                   lf.content_signature      AS Signature,
                   COUNT(fi.id)              AS InstanceCount,
                   COALESCE(SUM(fi.size), 0) AS TotalSize,
                   COALESCE(MAX(fi.size), 0) AS MaxSize,
                   MIN(fi.id)                AS SampleInstanceId
            FROM logical_files lf
            JOIN file_instances fi ON fi.logical_file_id = lf.id
            WHERE fi.volume_id = @VolumeId AND fi.status = 'active'
            GROUP BY lf.id, lf.content_signature_kind, lf.content_signature
            ORDER BY (COALESCE(SUM(fi.size), 0) - COALESCE(MAX(fi.size), 0)) DESC, COUNT(fi.id) DESC;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<SummaryRow>(new CommandDefinition(
            sql, new { VolumeId = volumeId.Value }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(r => new LogicalFileSummary(
            new LogicalFileId(r.Id),
            new ContentSignature(LogicalFileEnums.KindFromDb(r.Kind), r.Signature),
            (int)r.InstanceCount,
            r.TotalSize,
            r.MaxSize,
            new FileInstanceId(r.SampleInstanceId))).ToList();
    }

    private sealed record SummaryRow(string Id, string Kind, string Signature, long InstanceCount, long TotalSize, long MaxSize, string SampleInstanceId);

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
