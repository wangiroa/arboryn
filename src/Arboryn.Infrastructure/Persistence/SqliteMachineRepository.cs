using System.Globalization;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Database;
using Dapper;

namespace Arboryn.Infrastructure.Persistence;

/// <summary>
/// Dépôt SQLite des machines (Inc 13). L'identité est le nom d'hôte (index unique) ; le
/// libellé <c>name</c> est éditable et n'est jamais écrasé après création (préserve un
/// renommage utilisateur). La table est créée par la migration 003.
/// </summary>
public sealed class SqliteMachineRepository : IMachineRepository
{
    private readonly DatabaseFactory _databaseFactory;

    public SqliteMachineRepository(DatabaseFactory databaseFactory)
        => _databaseFactory = databaseFactory;

    private const string SelectColumns = """
        SELECT id            AS Id,
               name          AS Name,
               hostname      AS Hostname,
               first_seen_at AS FirstSeenAt,
               last_seen_at  AS LastSeenAt
        FROM machines
        """;

    public async Task<MachineId> EnsureLocalAsync(string hostname, CancellationToken cancellationToken)
    {
        // Crée la ligne si absente (nom initial = hostname), sinon met seulement à jour
        // last_seen_at — le libellé est préservé. L'id définitif (existant ou neuf) est
        // ensuite relu par hostname.
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        const string upsert = """
            INSERT INTO machines (id, name, hostname, first_seen_at, last_seen_at)
            VALUES (@Id, @Name, @Hostname, @Now, @Now)
            ON CONFLICT(hostname) DO UPDATE SET last_seen_at = excluded.last_seen_at;
            """;

        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            upsert,
            new { Id = MachineId.New().Value, Name = hostname, Hostname = hostname, Now = now },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var id = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT id FROM machines WHERE hostname = @Hostname;",
            new { Hostname = hostname },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return new MachineId(id!);
    }

    public async Task<MachineRecord?> GetAsync(MachineId id, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<MachineRow>(new CommandDefinition(
            $"{SelectColumns} WHERE id = @Id;",
            new { Id = id.Value },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<MachineRecord>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<MachineRow>(new CommandDefinition(
            $"{SelectColumns} ORDER BY name;",
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    public async Task RenameAsync(MachineId id, string newName, CancellationToken cancellationToken)
    {
        await using var connection = await _databaseFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE machines SET name = @Name WHERE id = @Id;",
            new { Id = id.Value, Name = newName },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static MachineRecord Map(MachineRow r) => new(
        new MachineId(r.Id),
        r.Name,
        r.Hostname,
        DateTime.Parse(r.FirstSeenAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        ParseNullable(r.LastSeenAt));

    private static DateTime? ParseNullable(string? value) =>
        string.IsNullOrEmpty(value)
            ? null
            : DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record MachineRow(
        string Id,
        string Name,
        string Hostname,
        string FirstSeenAt,
        string? LastSeenAt);
}
