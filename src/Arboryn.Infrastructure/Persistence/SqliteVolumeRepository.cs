using System.Globalization;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Database;
using Dapper;

namespace Arboryn.Infrastructure.Persistence;

/// <summary>
/// Dépôt SQLite des volumes (Inc 9). La clé est l'<see cref="VolumeId"/> ; le
/// numéro de série (VSN NTFS) et l'empreinte SMB servent à reconnaître un support
/// rebranché. La ligne « default » est créée par la migration initiale.
/// </summary>
public sealed class SqliteVolumeRepository : IVolumeRepository
{
    private readonly DatabaseFactory _databaseFactory;

    public SqliteVolumeRepository(DatabaseFactory databaseFactory)
        => _databaseFactory = databaseFactory;

    private const string SelectColumns = """
        SELECT id                   AS Id,
               name                 AS Name,
               kind                 AS Kind,
               serial               AS Serial,
               fingerprint          AS Fingerprint,
               label                AS Label,
               mount_point          AS MountPoint,
               last_usn             AS LastUsn,
               last_seen_at         AS LastSeenAt,
               last_scan_at         AS LastScanAt,
               status               AS Status,
               replication_scope_id AS ReplicationScopeId
        FROM volumes
        """;

    public async Task<VolumeRecord?> GetAsync(VolumeId id, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<VolumeRow>(new CommandDefinition(
            $"{SelectColumns} WHERE id = @Id;",
            new { Id = id.Value },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<VolumeRecord?> FindBySerialAsync(string serial, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<VolumeRow>(new CommandDefinition(
            $"{SelectColumns} WHERE serial = @Serial LIMIT 1;",
            new { Serial = serial },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<VolumeRecord?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<VolumeRow>(new CommandDefinition(
            $"{SelectColumns} WHERE fingerprint = @Fingerprint LIMIT 1;",
            new { Fingerprint = fingerprint },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<VolumeRecord>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<VolumeRow>(new CommandDefinition(
            $"{SelectColumns} ORDER BY name;",
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    public async Task UpsertAsync(VolumeRecord record, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO volumes
                (id, name, kind, serial, fingerprint, label, mount_point, last_usn,
                 last_seen_at, last_scan_at, status, replication_scope_id)
            VALUES
                (@Id, @Name, @Kind, @Serial, @Fingerprint, @Label, @MountPoint, @LastUsn,
                 @LastSeenAt, @LastScanAt, @Status, @ReplicationScopeId)
            ON CONFLICT(id) DO UPDATE SET
                name                 = excluded.name,
                kind                 = excluded.kind,
                serial               = excluded.serial,
                fingerprint          = excluded.fingerprint,
                label                = excluded.label,
                mount_point          = excluded.mount_point,
                last_usn             = excluded.last_usn,
                last_seen_at         = excluded.last_seen_at,
                last_scan_at         = excluded.last_scan_at,
                status               = excluded.status,
                replication_scope_id = excluded.replication_scope_id;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = record.Id.Value,
            record.Name,
            Kind = ToDbKind(record.Kind),
            record.Serial,
            record.Fingerprint,
            record.Label,
            record.MountPoint,
            record.LastUsn,
            LastSeenAt = ToIso(record.LastSeenAt),
            LastScanAt = ToIso(record.LastScanAt),
            Status = ToDbStatus(record.Status),
            record.ReplicationScopeId,
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task SetStatusAsync(VolumeId id, VolumeStatus status, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE volumes SET status = @Status WHERE id = @Id;",
            new { Id = id.Value, Status = ToDbStatus(status) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task RecordScanAsync(VolumeId id, DateTime scannedAtUtc, long? lastUsn, CancellationToken cancellationToken)
    {
        // last_usn n'est mis à jour que si une valeur est fournie (NTFS) : on préserve
        // la position connue sur les volumes sans USN Journal.
        const string sql = """
            UPDATE volumes
            SET last_scan_at = @ScannedAt,
                last_seen_at = @ScannedAt,
                last_usn     = COALESCE(@LastUsn, last_usn)
            WHERE id = @Id;
            """;
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id.Value, ScannedAt = ToIso(scannedAtUtc), LastUsn = lastUsn },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static VolumeRecord Map(VolumeRow r) => new(
        new VolumeId(r.Id),
        r.Name,
        FromDbKind(r.Kind),
        FromDbStatus(r.Status))
    {
        Serial = r.Serial,
        Fingerprint = r.Fingerprint,
        Label = r.Label,
        MountPoint = r.MountPoint,
        LastUsn = r.LastUsn,
        LastSeenAt = ParseNullable(r.LastSeenAt),
        LastScanAt = ParseNullable(r.LastScanAt),
        ReplicationScopeId = r.ReplicationScopeId,
    };

    private static string ToDbKind(VolumeKind kind) => kind switch
    {
        VolumeKind.Internal => "internal",
        VolumeKind.External => "external",
        VolumeKind.Nas => "nas",
        VolumeKind.Default => "default",
        _ => "other",
    };

    private static VolumeKind FromDbKind(string kind) => kind switch
    {
        "internal" => VolumeKind.Internal,
        "external" => VolumeKind.External,
        "nas" => VolumeKind.Nas,
        "default" => VolumeKind.Default,
        _ => VolumeKind.Other,
    };

    private static string ToDbStatus(VolumeStatus status) => status switch
    {
        VolumeStatus.Online => "online",
        VolumeStatus.Offline => "offline",
        _ => "unknown",
    };

    private static VolumeStatus FromDbStatus(string status) => status switch
    {
        "online" => VolumeStatus.Online,
        "offline" => VolumeStatus.Offline,
        _ => VolumeStatus.Unknown,
    };

    private static string? ToIso(DateTime? value) =>
        value is { } v ? v.ToString("O", CultureInfo.InvariantCulture) : null;

    private static DateTime? ParseNullable(string? value) =>
        string.IsNullOrEmpty(value)
            ? null
            : DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record VolumeRow(
        string Id,
        string Name,
        string Kind,
        string? Serial,
        string? Fingerprint,
        string? Label,
        string? MountPoint,
        long? LastUsn,
        string? LastSeenAt,
        string? LastScanAt,
        string Status,
        string? ReplicationScopeId);
}
