using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Persistence;
using Dapper;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

/// <summary>
/// Tests du dépôt des machines (Inc 13) et du bon déploiement de la migration 003
/// (table <c>machines</c> + colonne <c>volumes.machine_id</c>).
/// </summary>
public class MachineRepositoryTests
{
    [Fact]
    public async Task EnsureLocal_IsIdempotentOnHostname()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteMachineRepository(db.Factory);

        var first = await repo.EnsureLocalAsync("ALICE-PC", CancellationToken.None);
        var second = await repo.EnsureLocalAsync("ALICE-PC", CancellationToken.None);

        second.Should().Be(first);                       // même id pour le même hôte
        var all = await repo.GetAllAsync(CancellationToken.None);
        all.Should().ContainSingle();
        all[0].Name.Should().Be("ALICE-PC");             // libellé initial = hostname
        all[0].Hostname.Should().Be("ALICE-PC");
    }

    [Fact]
    public async Task Rename_ChangesLabel_NotHostname()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteMachineRepository(db.Factory);
        var id = await repo.EnsureLocalAsync("HOST1", CancellationToken.None);

        await repo.RenameAsync(id, "PC du salon", CancellationToken.None);

        var machine = await repo.GetAsync(id, CancellationToken.None);
        machine.Should().NotBeNull();
        machine!.Name.Should().Be("PC du salon");
        machine.Hostname.Should().Be("HOST1");           // identité stable inchangée
    }

    [Fact]
    public async Task EnsureLocal_PreservesUserRename_OnReconnect()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteMachineRepository(db.Factory);
        var id = await repo.EnsureLocalAsync("HOST2", CancellationToken.None);
        await repo.RenameAsync(id, "Bureau", CancellationToken.None);

        await repo.EnsureLocalAsync("HOST2", CancellationToken.None);   // nouvel enrôlement

        var machine = await repo.GetAsync(id, CancellationToken.None);
        machine!.Name.Should().Be("Bureau");             // le libellé n'est pas réécrasé
    }

    [Fact]
    public async Task Migration003_CreatesMachinesTable_AndVolumesMachineIdColumn()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var connection = await db.Factory.OpenAsync(CancellationToken.None);

        var machineRows = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM machines;");
        machineRows.Should().Be(0);

        var columns = (await connection.QueryAsync<string>("SELECT name FROM pragma_table_info('volumes');")).ToList();
        columns.Should().Contain("machine_id");
    }
}
