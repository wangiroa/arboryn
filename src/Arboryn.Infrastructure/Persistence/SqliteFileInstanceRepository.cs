using System.Globalization;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Database;
using Dapper;

namespace Arboryn.Infrastructure.Persistence;

/// <summary>
/// Dépôt SQLite des <see cref="FileInstanceRecord"/>. La clé naturelle est
/// <c>(volume_id, relative_path)</c> : un re-scan met à jour l'instance existante
/// plutôt que d'en créer une nouvelle. La détection de doublons exacts (Inc 1)
/// s'appuie sur l'index <c>idx_file_instances_canonical_size</c>.
/// </summary>
public sealed class SqliteFileInstanceRepository
    : IFileInstanceRepository, IFileHashStore, IFileInstanceLinker, IPerceptualHashStore
{
    private readonly DatabaseFactory _databaseFactory;

    public SqliteFileInstanceRepository(DatabaseFactory databaseFactory)
        => _databaseFactory = databaseFactory;

    public async Task<FileInstanceId> UpsertAsync(FileInstanceRecord record, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO file_instances
                (id, volume_id, relative_path, canonical_name, size, modified_at, logical_file_id, status, last_seen_at)
            VALUES
                (@Id, @VolumeId, @RelativePath, @CanonicalName, @Size, @ModifiedAt, @LogicalFileId, 'active', datetime('now'))
            ON CONFLICT(volume_id, relative_path) DO UPDATE SET
                canonical_name  = excluded.canonical_name,
                size            = excluded.size,
                modified_at     = excluded.modified_at,
                -- Préserve le rattachement existant si le nouvel upsert ne précise pas de LF.
                logical_file_id = COALESCE(excluded.logical_file_id, file_instances.logical_file_id),
                -- Invalide les empreintes mémorisées si le fichier a changé.
                sha256          = CASE
                                      WHEN file_instances.size <> excluded.size
                                        OR file_instances.modified_at <> excluded.modified_at
                                      THEN NULL ELSE file_instances.sha256 END,
                phash           = CASE
                                      WHEN file_instances.size <> excluded.size
                                        OR file_instances.modified_at <> excluded.modified_at
                                      THEN NULL ELSE file_instances.phash END,
                status          = 'active',
                last_seen_at    = datetime('now')
            RETURNING id;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var actualId = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(sql, new
        {
            Id = record.Id.Value,
            VolumeId = record.VolumeId.Value,
            // En Inc 1 (volume « default »), relative_path porte le chemin absolu.
            RelativePath = record.Path.Value,
            CanonicalName = record.CanonicalName.Value,
            record.Size,
            ModifiedAt = ToIso(record.ModifiedAt),
            LogicalFileId = record.LogicalFileId?.Value,
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return new FileInstanceId(actualId ?? record.Id.Value);
    }

    public Task<IReadOnlyList<FileInstanceRecord>> GetDuplicateCandidatesAsync(
        VolumeId volumeId, CancellationToken cancellationToken)
        => GetDuplicateCandidatesAsync(volumeId, underRoot: null, cancellationToken);

    public async Task<IReadOnlyList<FileInstanceRecord>> GetDuplicateCandidatesAsync(
        VolumeId volumeId, FilePath? underRoot, CancellationToken cancellationToken)
    {
        // Inc 3 : on regroupe par logical_file_id s'il est défini, sinon par
        // canonical_name||'|'||size (équivalent à la signature « name_size »). Permet
        // de détecter aussi les instances unifiées par hash (LFs sha256) au-delà du
        // simple match nom+taille, tout en restant compatible avec un catalogue non
        // encore rattaché.
        const string sql = """
            SELECT fi.id             AS Id,
                   fi.volume_id      AS VolumeId,
                   fi.relative_path  AS RelativePath,
                   fi.canonical_name AS CanonicalName,
                   fi.size           AS Size,
                   fi.modified_at    AS ModifiedAt
            FROM file_instances fi
            JOIN (
                SELECT COALESCE(logical_file_id, canonical_name || '|' || size) AS group_key
                FROM file_instances
                WHERE volume_id = @VolumeId AND status = 'active'
                  AND (@Root IS NULL OR substr(lower(relative_path), 1, @RootLen) = @Root)
                GROUP BY group_key
                HAVING COUNT(*) > 1
            ) dup ON COALESCE(fi.logical_file_id, fi.canonical_name || '|' || fi.size) = dup.group_key
            WHERE fi.volume_id = @VolumeId AND fi.status = 'active'
              AND (@Root IS NULL OR substr(lower(fi.relative_path), 1, @RootLen) = @Root)
            ORDER BY COALESCE(fi.logical_file_id, fi.canonical_name || '|' || fi.size), fi.relative_path;
            """;

        string? root = null;
        var rootLen = 0;
        if (underRoot is { } r)
        {
            root = (r.Value + "\\").ToLowerInvariant();
            rootLen = root.Length;
        }

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<InstanceRow>(new CommandDefinition(
            sql,
            new { VolumeId = volumeId.Value, Root = root, RootLen = rootLen },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<FileInstanceRecord>> GetActiveInstancesAsync(
        VolumeId volumeId, FilePath? underRoot, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id             AS Id,
                   volume_id      AS VolumeId,
                   relative_path  AS RelativePath,
                   canonical_name AS CanonicalName,
                   size           AS Size,
                   modified_at    AS ModifiedAt
            FROM file_instances
            WHERE volume_id = @VolumeId AND status = 'active'
              AND (@Root IS NULL OR substr(lower(relative_path), 1, @RootLen) = @Root)
            ORDER BY canonical_name;
            """;

        string? root = null;
        var rootLen = 0;
        if (underRoot is { } r)
        {
            root = (r.Value + "\\").ToLowerInvariant();
            rootLen = root.Length;
        }

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<InstanceRow>(new CommandDefinition(
            sql,
            new { VolumeId = volumeId.Value, Root = root, RootLen = rootLen },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(Map).ToList();
    }

    public async Task ClearVolumeAsync(VolumeId volumeId, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM file_instances WHERE volume_id = @VolumeId;",
            new { VolumeId = volumeId.Value },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public Task MarkDeletedAsync(FileInstanceId id, CancellationToken cancellationToken)
        => SetStatusAsync(id, "deleted", cancellationToken);

    public Task MarkActiveAsync(FileInstanceId id, CancellationToken cancellationToken)
        => SetStatusAsync(id, "active", cancellationToken);

    private async Task SetStatusAsync(FileInstanceId id, string status, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE file_instances SET status = @Status, last_seen_at = datetime('now') WHERE id = @Id;",
            new { Status = status, Id = id.Value },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<Sha256?> GetAsync(FileInstanceId id, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var hex = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT sha256 FROM file_instances WHERE id = @Id;",
            new { Id = id.Value },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return string.IsNullOrEmpty(hex) ? null : Sha256.FromHex(hex);
    }

    public async Task SetLogicalFileAsync(FileInstanceId id, LogicalFileId logicalFileId, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE file_instances SET logical_file_id = @LogicalFileId WHERE id = @Id;",
            new { LogicalFileId = logicalFileId.Value, Id = id.Value },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task SetAsync(FileInstanceId id, Sha256 hash, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE file_instances SET sha256 = @Hash WHERE id = @Id;",
            new { Hash = hash.Value, Id = id.Value },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    // --- IPerceptualHashStore -------------------------------------------------

    public async Task<IReadOnlyList<FileInstanceRecord>> GetWithoutPerceptualHashAsync(
        VolumeId volumeId, FilePath? underRoot, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id             AS Id,
                   volume_id      AS VolumeId,
                   relative_path  AS RelativePath,
                   canonical_name AS CanonicalName,
                   size           AS Size,
                   modified_at    AS ModifiedAt
            FROM file_instances
            WHERE volume_id = @VolumeId AND status = 'active' AND phash IS NULL
              AND (@Root IS NULL OR substr(lower(relative_path), 1, @RootLen) = @Root)
            ORDER BY canonical_name;
            """;

        var (root, rootLen) = RootFilter(underRoot);

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<InstanceRow>(new CommandDefinition(
            sql,
            new { VolumeId = volumeId.Value, Root = root, RootLen = rootLen },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(Map).ToList();
    }

    public async Task SetAsync(FileInstanceId id, PerceptualHash hash, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE file_instances SET phash = @Hash WHERE id = @Id;",
            new { Hash = hash.ToHex(), Id = id.Value },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PerceptualHashedInstance>> GetHashedAsync(
        VolumeId volumeId, FilePath? underRoot, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id             AS Id,
                   volume_id      AS VolumeId,
                   relative_path  AS RelativePath,
                   canonical_name AS CanonicalName,
                   size           AS Size,
                   modified_at    AS ModifiedAt,
                   phash          AS Phash
            FROM file_instances
            WHERE volume_id = @VolumeId AND status = 'active' AND phash IS NOT NULL
              AND (@Root IS NULL OR substr(lower(relative_path), 1, @RootLen) = @Root)
            ORDER BY canonical_name;
            """;

        var (root, rootLen) = RootFilter(underRoot);

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<HashedRow>(new CommandDefinition(
            sql,
            new { VolumeId = volumeId.Value, Root = root, RootLen = rootLen },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows
            .Select(r => new PerceptualHashedInstance(Map(r), PerceptualHash.FromHex(r.Phash)))
            .ToList();
    }

    private static (string? Root, int RootLen) RootFilter(FilePath? underRoot)
    {
        if (underRoot is { } r)
        {
            var root = (r.Value + "\\").ToLowerInvariant();
            return (root, root.Length);
        }

        return (null, 0);
    }

    private static FileInstanceRecord Map(InstanceRow r) => new(
        new FileInstanceId(r.Id),
        new VolumeId(r.VolumeId),
        FilePath.From(r.RelativePath),
        new CanonicalName(r.CanonicalName),
        r.Size,
        DateTime.Parse(r.ModifiedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    private static FileInstanceRecord Map(HashedRow r) => new(
        new FileInstanceId(r.Id),
        new VolumeId(r.VolumeId),
        FilePath.From(r.RelativePath),
        new CanonicalName(r.CanonicalName),
        r.Size,
        DateTime.Parse(r.ModifiedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    private static string ToIso(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    private sealed record InstanceRow(
        string Id,
        string VolumeId,
        string RelativePath,
        string CanonicalName,
        long Size,
        string ModifiedAt);

    private sealed record HashedRow(
        string Id,
        string VolumeId,
        string RelativePath,
        string CanonicalName,
        long Size,
        string ModifiedAt,
        string Phash);
}
