using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Replication;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class ReplicationScopeRepositoryTests
{
    [Fact]
    public async Task Upsert_Then_Get_RoundTripsCompositeExpression()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteReplicationScopeRepository(db.Factory);
        var id = ScopeId.New();
        // category = 'Documents officiels' AND subcategory = 'Investissements'
        var expression = ScopeExpression.And(
            ScopeExpression.Categories(MediaCategory.OfficialDocument),
            ScopeExpression.Subcategories("Investissements"),
            ScopeExpression.Not(ScopeExpression.Years(null, 1999)));
        await repo.UpsertAsync(new ReplicationScope(id, "PC perso — Investissements", expression), CancellationToken.None);

        var fetched = await repo.GetAsync(id, CancellationToken.None);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("PC perso — Investissements");
        // Preuve d'équivalence : mêmes verdicts qu'en mémoire sur des sujets représentatifs.
        fetched.Expression.Matches(new ScopeSubject(MediaCategory.OfficialDocument, "Investissements/Factures", 2024))
            .Should().BeTrue();
        fetched.Expression.Matches(new ScopeSubject(MediaCategory.OfficialDocument, "Fiscal", 2024))
            .Should().BeFalse();
        fetched.Expression.Matches(new ScopeSubject(MediaCategory.OfficialDocument, "Investissements", 1998))
            .Should().BeFalse();
    }

    [Fact]
    public async Task RoundTrip_PreservesAllLeafKinds()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteReplicationScopeRepository(db.Factory);
        var all = ScopeExpression.All;
        var complex = ScopeExpression.Or(
            ScopeExpression.All,
            ScopeExpression.None,
            ScopeExpression.Categories(MediaCategory.Audiobook, MediaCategory.Book),
            ScopeExpression.Years(2000, 2010));

        var allId = ScopeId.New();
        var complexId = ScopeId.New();
        await repo.UpsertAsync(new ReplicationScope(allId, "Tout", all), CancellationToken.None);
        await repo.UpsertAsync(new ReplicationScope(complexId, "Mix", complex), CancellationToken.None);

        // Les nœuds à liste ne comparent pas structurellement (égalité de record = référence
        // sur IReadOnlyList) : on prouve la fidélité du round-trip via la forme sérialisée.
        var json = ScopeExpressionJson.Serialize(complex);
        ScopeExpressionJson.Serialize(ScopeExpressionJson.Deserialize(json)).Should().Be(json);
        (await repo.GetAsync(allId, CancellationToken.None))!.Expression.Should().Be(all); // singleton
        ScopeExpressionJson.Serialize((await repo.GetAsync(complexId, CancellationToken.None))!.Expression)
            .Should().Be(json);
    }

    [Fact]
    public async Task GetAll_And_Delete_Work()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteReplicationScopeRepository(db.Factory);
        var a = ScopeId.New();
        var b = ScopeId.New();
        await repo.UpsertAsync(new ReplicationScope(a, "A", ScopeExpression.All), CancellationToken.None);
        await repo.UpsertAsync(new ReplicationScope(b, "B", ScopeExpression.None), CancellationToken.None);

        (await repo.GetAllAsync(CancellationToken.None)).Should().HaveCount(2);

        await repo.DeleteAsync(a, CancellationToken.None);

        (await repo.GetAllAsync(CancellationToken.None)).Should().ContainSingle(s => s.Id == b);
        (await repo.GetAsync(a, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Volume_CanBeAssignedScope_AndDeletingScopeNullsTheLink()
    {
        await using var db = await TestDatabase.CreateAsync();
        var scopes = new SqliteReplicationScopeRepository(db.Factory);
        var volumes = new SqliteVolumeRepository(db.Factory);
        var scopeId = ScopeId.New();
        await scopes.UpsertAsync(
            new ReplicationScope(scopeId, "NAS — tout", ScopeExpression.All), CancellationToken.None);

        var volId = VolumeId.New();
        await volumes.UpsertAsync(
            new VolumeRecord(volId, "NAS", VolumeKind.Nas, VolumeStatus.Online)
            {
                ReplicationScopeId = scopeId.Value,
            },
            CancellationToken.None);

        (await volumes.GetAsync(volId, CancellationToken.None))!.ReplicationScopeId.Should().Be(scopeId.Value);

        // ON DELETE SET NULL : supprimer le scope détache le volume sans erreur de FK.
        await scopes.DeleteAsync(scopeId, CancellationToken.None);

        (await volumes.GetAsync(volId, CancellationToken.None))!.ReplicationScopeId.Should().BeNull();
    }

    [Fact]
    public async Task Upsert_OnExistingId_UpdatesExpression()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteReplicationScopeRepository(db.Factory);
        var id = ScopeId.New();
        await repo.UpsertAsync(new ReplicationScope(id, "Scope", ScopeExpression.None), CancellationToken.None);

        await repo.UpsertAsync(
            new ReplicationScope(id, "Scope (édité)", ScopeExpression.Categories(MediaCategory.Video)),
            CancellationToken.None);

        var all = await repo.GetAllAsync(CancellationToken.None);
        all.Should().ContainSingle();
        var fetched = all[0];
        fetched.Name.Should().Be("Scope (édité)");
        fetched.Expression.Matches(new ScopeSubject(MediaCategory.Video)).Should().BeTrue();
    }
}
