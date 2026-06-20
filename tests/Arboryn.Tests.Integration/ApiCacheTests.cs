using Arboryn.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class ApiCacheTests
{
    [Fact]
    public async Task SetThenGet_ReturnsStoredResponse()
    {
        await using var db = await TestDatabase.CreateAsync();
        var cache = new SqliteApiCache(db.Factory);

        await cache.SetAsync("openlibrary", "hash1", "{\"a\":1}", TimeSpan.FromDays(1), CancellationToken.None);

        (await cache.GetAsync("openlibrary", "hash1", CancellationToken.None)).Should().Be("{\"a\":1}");
        (await cache.GetAsync("openlibrary", "other", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Get_ReturnsNull_WhenExpired()
    {
        await using var db = await TestDatabase.CreateAsync();
        var cache = new SqliteApiCache(db.Factory);

        await cache.SetAsync("tmdb", "h", "{}", TimeSpan.FromSeconds(-1), CancellationToken.None);

        (await cache.GetAsync("tmdb", "h", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Set_Overwrites_OnConflict()
    {
        await using var db = await TestDatabase.CreateAsync();
        var cache = new SqliteApiCache(db.Factory);

        await cache.SetAsync("p", "h", "v1", null, CancellationToken.None);
        await cache.SetAsync("p", "h", "v2", null, CancellationToken.None);

        (await cache.GetAsync("p", "h", CancellationToken.None)).Should().Be("v2");
    }
}
