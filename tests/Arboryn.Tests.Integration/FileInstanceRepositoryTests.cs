using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Persistence;
using Dapper;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class FileInstanceRepositoryTests
{
    [Fact]
    public async Task Upsert_OnSameRelativePath_UpdatesInsteadOfDuplicating()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteFileInstanceRepository(db.Factory);
        var record = MakeRecord(@"C:\lib\books\a.epub", "a.epub", size: 100);

        await repo.UpsertAsync(record, CancellationToken.None);
        await repo.UpsertAsync(record with { Size = 250 }, CancellationToken.None);

        await using var connection = await db.Factory.OpenAsync();
        var count = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM file_instances;");
        var size = await connection.ExecuteScalarAsync<long>("SELECT size FROM file_instances;");

        count.Should().Be(1);
        size.Should().Be(250);
    }

    [Fact]
    public async Task GetDuplicateCandidates_ReturnsOnlyInstancesSharingNameAndSize()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteFileInstanceRepository(db.Factory);

        await repo.UpsertAsync(MakeRecord(@"C:\x\book.epub", "book.epub", 100), CancellationToken.None);
        await repo.UpsertAsync(MakeRecord(@"C:\y\book.epub", "book.epub", 100), CancellationToken.None); // doublon
        await repo.UpsertAsync(MakeRecord(@"C:\z\book.epub", "book.epub", 999), CancellationToken.None); // taille différente
        await repo.UpsertAsync(MakeRecord(@"C:\w\unique.pdf", "unique.pdf", 50), CancellationToken.None); // unique

        var candidates = await repo.GetDuplicateCandidatesAsync(VolumeId.Default, CancellationToken.None);

        candidates.Should().HaveCount(2);
        candidates.Select(c => c.Path.Value)
            .Should().BeEquivalentTo(@"C:\x\book.epub", @"C:\y\book.epub");
    }

    [Fact]
    public async Task GetDuplicateCandidates_UnderRoot_FiltersByPathPrefix()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteFileInstanceRepository(db.Factory);
        await repo.UpsertAsync(MakeRecord(@"C:\scanA\book.epub", "book.epub", 100), CancellationToken.None);
        await repo.UpsertAsync(MakeRecord(@"C:\scanA\sub\book.epub", "book.epub", 100), CancellationToken.None);
        await repo.UpsertAsync(MakeRecord(@"C:\scanB\book.epub", "book.epub", 100), CancellationToken.None);

        // Tout le catalogue : les 3 partagent (book.epub, 100).
        var all = await repo.GetDuplicateCandidatesAsync(VolumeId.Default, underRoot: null, CancellationToken.None);
        all.Should().HaveCount(3);

        // Sous C:\scanA : seules les 2 copies de ce sous-arbre forment un groupe.
        var underA = await repo.GetDuplicateCandidatesAsync(
            VolumeId.Default, FilePath.From(@"C:\scanA"), CancellationToken.None);
        underA.Should().HaveCount(2);
        underA.Select(c => c.Path.Value).Should().OnlyContain(p => p.StartsWith(@"C:\scanA"));
    }

    [Fact]
    public async Task GetDuplicateCandidates_NoDuplicates_ReturnsEmpty()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteFileInstanceRepository(db.Factory);
        await repo.UpsertAsync(MakeRecord(@"C:\a\only.pdf", "only.pdf", 1), CancellationToken.None);

        var candidates = await repo.GetDuplicateCandidatesAsync(VolumeId.Default, CancellationToken.None);

        candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveInstances_ReturnsAllActive_RespectingRootFilter()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteFileInstanceRepository(db.Factory);
        await repo.UpsertAsync(MakeRecord(@"C:\scanA\a.pdf", "a.pdf", 1), CancellationToken.None);
        await repo.UpsertAsync(MakeRecord(@"C:\scanA\sub\b.pdf", "b.pdf", 2), CancellationToken.None);
        await repo.UpsertAsync(MakeRecord(@"C:\scanB\c.pdf", "c.pdf", 3), CancellationToken.None);

        var all = await repo.GetActiveInstancesAsync(VolumeId.Default, null, CancellationToken.None);
        all.Should().HaveCount(3);

        var underA = await repo.GetActiveInstancesAsync(VolumeId.Default, FilePath.From(@"C:\scanA"), CancellationToken.None);
        underA.Should().HaveCount(2);
    }

    [Fact]
    public async Task ClearVolume_RemovesAllInstancesOfThatVolume()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteFileInstanceRepository(db.Factory);
        await repo.UpsertAsync(MakeRecord(@"C:\a\one.pdf", "one.pdf", 1), CancellationToken.None);
        await repo.UpsertAsync(MakeRecord(@"C:\b\two.pdf", "two.pdf", 2), CancellationToken.None);

        await repo.ClearVolumeAsync(VolumeId.Default, CancellationToken.None);

        await using var connection = await db.Factory.OpenAsync();
        var count = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM file_instances WHERE volume_id = @v;",
            new { v = VolumeId.Default.Value });
        count.Should().Be(0);
    }

    private static FileInstanceRecord MakeRecord(string absolutePath, string canonical, long size) => new(
        FileInstanceId.New(),
        VolumeId.Default,
        FilePath.From(absolutePath),
        CanonicalName.From(canonical),
        size,
        DateTime.UtcNow);
}
