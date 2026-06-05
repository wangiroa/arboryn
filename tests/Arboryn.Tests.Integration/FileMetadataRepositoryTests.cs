using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class FileMetadataRepositoryTests
{
    [Fact]
    public async Task Upsert_ThenGet_RoundTrips()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var record = MakeInstance();
        await instances.UpsertAsync(record, CancellationToken.None);

        var now = new DateTime(2026, 5, 28, 12, 0, 0, DateTimeKind.Utc);
        await metadata.UpsertAsync(new MetadataEntry(
            record.Id, MetadataKeys.Title, "Hamlet", MetadataSources.Filename, 0.5, now),
            CancellationToken.None);

        var all = await metadata.GetForInstanceAsync(record.Id, CancellationToken.None);
        all.Should().ContainSingle();
        all[0].Value.Should().Be("Hamlet");
        all[0].Source.Should().Be(MetadataSources.Filename);
        all[0].Confidence.Should().Be(0.5);
    }

    [Fact]
    public async Task Upsert_SameSource_Overwrites()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var record = MakeInstance();
        await instances.UpsertAsync(record, CancellationToken.None);

        var t0 = new DateTime(2026, 5, 28, 10, 0, 0, DateTimeKind.Utc);
        var t1 = new DateTime(2026, 5, 28, 11, 0, 0, DateTimeKind.Utc);
        await metadata.UpsertAsync(new MetadataEntry(
            record.Id, MetadataKeys.Title, "Old", MetadataSources.Filename, 0.5, t0), CancellationToken.None);
        await metadata.UpsertAsync(new MetadataEntry(
            record.Id, MetadataKeys.Title, "New", MetadataSources.Filename, 0.7, t1), CancellationToken.None);

        var all = await metadata.GetForInstanceAsync(record.Id, CancellationToken.None);
        all.Should().ContainSingle();
        all[0].Value.Should().Be("New");
        all[0].Confidence.Should().Be(0.7);
    }

    [Fact]
    public async Task GetFused_PicksHighestConfidencePerKey()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var record = MakeInstance();
        await instances.UpsertAsync(record, CancellationToken.None);

        var t = DateTime.UtcNow;
        // Même clé, deux sources : la plus haute confiance gagne (ID3 0.95 > filename 0.5).
        await metadata.UpsertAsync(new MetadataEntry(
            record.Id, MetadataKeys.Title, "Hamlet (filename)", MetadataSources.Filename, 0.5, t), CancellationToken.None);
        await metadata.UpsertAsync(new MetadataEntry(
            record.Id, MetadataKeys.Title, "Hamlet (ID3)", MetadataSources.Id3, 0.95, t), CancellationToken.None);
        // Autre clé : une seule source.
        await metadata.UpsertAsync(new MetadataEntry(
            record.Id, MetadataKeys.Artist, "Shakespeare", MetadataSources.Id3, 0.9, t), CancellationToken.None);

        var fused = await metadata.GetFusedAsync(record.Id, CancellationToken.None);

        fused.Should().HaveCount(2);
        fused[MetadataKeys.Title].Value.Should().Be("Hamlet (ID3)");
        fused[MetadataKeys.Title].Source.Should().Be(MetadataSources.Id3);
        fused[MetadataKeys.Artist].Value.Should().Be("Shakespeare");
    }

    [Fact]
    public async Task DeleteForInstance_RemovesAll()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var record = MakeInstance();
        await instances.UpsertAsync(record, CancellationToken.None);
        var t = DateTime.UtcNow;
        await metadata.UpsertAsync(new MetadataEntry(record.Id, "k1", "v1", "src", 1.0, t), CancellationToken.None);
        await metadata.UpsertAsync(new MetadataEntry(record.Id, "k2", "v2", "src", 1.0, t), CancellationToken.None);

        await metadata.DeleteForInstanceAsync(record.Id, CancellationToken.None);

        (await metadata.GetForInstanceAsync(record.Id, CancellationToken.None)).Should().BeEmpty();
    }

    private static FileInstanceRecord MakeInstance() => new(
        FileInstanceId.New(),
        VolumeId.Default,
        FilePath.From(@"C:\library\hamlet.mp3"),
        CanonicalName.From("hamlet.mp3"),
        12345,
        new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
}
