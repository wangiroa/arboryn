using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class FileHashStoreTests
{
    [Fact]
    public async Task Set_ThenGet_RoundTripsHash()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteFileInstanceRepository(db.Factory);
        var record = MakeRecord(@"C:\a\f.pdf", "f.pdf", 100);
        await repo.UpsertAsync(record, CancellationToken.None);

        (await repo.GetAsync(record.Id, CancellationToken.None)).Should().BeNull();

        var hash = Sha256.FromHex(new string('a', 64));
        await repo.SetAsync(record.Id, hash, CancellationToken.None);

        (await repo.GetAsync(record.Id, CancellationToken.None)).Should().Be(hash);
    }

    [Fact]
    public async Task Upsert_InvalidatesHash_WhenSizeChanges()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteFileInstanceRepository(db.Factory);
        var record = MakeRecord(@"C:\a\f.pdf", "f.pdf", 100);
        await repo.UpsertAsync(record, CancellationToken.None);
        await repo.SetAsync(record.Id, Sha256.FromHex(new string('a', 64)), CancellationToken.None);

        // Re-scan avec une taille différente → l'empreinte doit être invalidée.
        await repo.UpsertAsync(record with { Size = 200 }, CancellationToken.None);

        (await repo.GetAsync(record.Id, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Upsert_PreservesHash_WhenUnchanged()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteFileInstanceRepository(db.Factory);
        var record = MakeRecord(@"C:\a\f.pdf", "f.pdf", 100);
        await repo.UpsertAsync(record, CancellationToken.None);
        var hash = Sha256.FromHex(new string('a', 64));
        await repo.SetAsync(record.Id, hash, CancellationToken.None);

        // Re-scan identique (même taille + date) → l'empreinte est conservée.
        await repo.UpsertAsync(record, CancellationToken.None);

        (await repo.GetAsync(record.Id, CancellationToken.None)).Should().Be(hash);
    }

    private static FileInstanceRecord MakeRecord(string absolutePath, string canonical, long size) => new(
        FileInstanceId.New(),
        VolumeId.Default,
        FilePath.From(absolutePath),
        CanonicalName.From(canonical),
        size,
        new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
}
