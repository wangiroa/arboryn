using Arboryn.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class SettingsRepositoryTests
{
    [Fact]
    public async Task Get_UnknownKey_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteSettingsRepository(db.Factory);

        var value = await repo.GetAsync("absent", CancellationToken.None);

        value.Should().BeNull();
    }

    [Fact]
    public async Task Set_ThenGet_RoundTrips_AndUpserts()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteSettingsRepository(db.Factory);

        await repo.SetAsync("priority_directories", "[\"C:\\\\Docs\"]", CancellationToken.None);
        (await repo.GetAsync("priority_directories", CancellationToken.None))
            .Should().Be("[\"C:\\\\Docs\"]");

        // Réécriture (UPSERT) de la même clé.
        await repo.SetAsync("priority_directories", "[]", CancellationToken.None);
        (await repo.GetAsync("priority_directories", CancellationToken.None)).Should().Be("[]");
    }
}
