using Arboryn.Application.Abstractions;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Persistence;
using Dapper;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class LogicalFileRepositoryTests
{
    [Fact]
    public async Task Upsert_ThenFindBySignature_RoundTrips()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteLogicalFileRepository(db.Factory);
        var signature = ContentSignature.NameSize(CanonicalName.From("book.epub"), 1234);
        var now = DateTime.UtcNow;
        var lf = new LogicalFile(LogicalFileId.New(), MediaCategory.Unknown, signature, now, now);

        await repo.UpsertAsync(lf, CancellationToken.None);

        var found = await repo.FindBySignatureAsync(signature, CancellationToken.None);
        found.Should().NotBeNull();
        found!.Id.Value.Should().Be(lf.Id.Value);
        found.Signature.Should().Be(signature);
        found.Category.Should().Be(MediaCategory.Unknown);
    }

    [Fact]
    public async Task FindBySignature_Unknown_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteLogicalFileRepository(db.Factory);

        var found = await repo.FindBySignatureAsync(
            ContentSignature.NameSize(CanonicalName.From("absent.pdf"), 1), CancellationToken.None);

        found.Should().BeNull();
    }

    [Fact]
    public async Task BackfillUnattached_CreatesAndAttachesLogicalFiles()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);

        // Trois instances dont deux partagent (canonical, size) → un seul LogicalFile attendu pour celles-là.
        await instances.UpsertAsync(Make(@"C:\a\book.epub", "book.epub", 100), CancellationToken.None);
        await instances.UpsertAsync(Make(@"C:\b\book.epub", "book.epub", 100), CancellationToken.None);
        await instances.UpsertAsync(Make(@"C:\c\other.pdf", "other.pdf", 50), CancellationToken.None);

        // Pré-condition : aucune n'est rattachée (upsert sans LogicalFileId).
        await using (var pre = await db.Factory.OpenAsync())
        {
            var unattached = await pre.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM file_instances WHERE logical_file_id IS NULL;");
            unattached.Should().Be(3);
        }

        await logicalFiles.BackfillUnattachedAsync(CancellationToken.None);

        await using var post = await db.Factory.OpenAsync();
        var stillUnattached = await post.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM file_instances WHERE logical_file_id IS NULL;");
        var logicalCount = await post.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM logical_files;");
        var sharedLfCount = await post.ExecuteScalarAsync<long>("""
            SELECT COUNT(DISTINCT logical_file_id) FROM file_instances
            WHERE relative_path IN (@A, @B);
            """, new { A = @"C:\a\book.epub", B = @"C:\b\book.epub" });

        stillUnattached.Should().Be(0);
        logicalCount.Should().Be(2, "deux signatures distinctes (book/100 et other/50)");
        sharedLfCount.Should().Be(1, "les deux 'book.epub' partagent le même LogicalFile");
    }

    [Fact]
    public async Task BackfillUnattached_IsIdempotent()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        await instances.UpsertAsync(Make(@"C:\a\f.pdf", "f.pdf", 10), CancellationToken.None);

        await logicalFiles.BackfillUnattachedAsync(CancellationToken.None);
        await logicalFiles.BackfillUnattachedAsync(CancellationToken.None);

        await using var conn = await db.Factory.OpenAsync();
        (await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM logical_files;")).Should().Be(1);
    }

    [Fact]
    public async Task GetDuplicateCandidates_GroupsByLogicalFileId_WhenAttached()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);

        // Deux fichiers de noms ET tailles différents, mais rattachés au MÊME LogicalFile
        // (cas typique post-promotion par hash : variantes confirmées identiques).
        var lfId = LogicalFileId.New();
        var lf = new LogicalFile(
            lfId, MediaCategory.Unknown,
            ContentSignature.FromSha256(Sha256.FromHex(new string('a', 64))),
            DateTime.UtcNow, DateTime.UtcNow);
        await logicalFiles.UpsertAsync(lf, CancellationToken.None);

        await instances.UpsertAsync(
            Make(@"C:\a\book.epub", "book.epub", 100) with { LogicalFileId = lfId }, CancellationToken.None);
        await instances.UpsertAsync(
            Make(@"C:\b\book_v2.epub", "book v2.epub", 150) with { LogicalFileId = lfId }, CancellationToken.None);

        var candidates = await instances.GetDuplicateCandidatesAsync(VolumeId.Default, CancellationToken.None);

        candidates.Should().HaveCount(2);
        candidates.Select(c => c.Path.Value)
            .Should().BeEquivalentTo(@"C:\a\book.epub", @"C:\b\book_v2.epub");
    }

    [Fact]
    public async Task GetMetricsAndSummaries_ReflectAttachedCatalog()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);

        await instances.UpsertAsync(Make(@"C:\a\book.epub", "book.epub", 100), CancellationToken.None);
        await instances.UpsertAsync(Make(@"C:\b\book.epub", "book.epub", 100), CancellationToken.None);
        await instances.UpsertAsync(Make(@"C:\c\other.pdf", "other.pdf", 50), CancellationToken.None);
        await logicalFiles.BackfillUnattachedAsync(CancellationToken.None);

        var metrics = await logicalFiles.GetMetricsAsync(VolumeId.Default, CancellationToken.None);
        metrics.FileInstances.Should().Be(3);
        metrics.LogicalFiles.Should().Be(2);
        metrics.RedundancyRatio.Should().BeApproximately(1.5, 0.01);

        var summaries = await logicalFiles.GetSummariesAsync(VolumeId.Default, CancellationToken.None);
        summaries.Should().HaveCount(2);
        var booksSummary = summaries.Single(s => s.InstanceCount == 2);
        booksSummary.TotalSize.Should().Be(200);
        booksSummary.MaxSize.Should().Be(100);
        booksSummary.ReclaimableBytes.Should().Be(100, "deux copies de 100, on garde la plus grosse");

        // Tri par espace récupérable décroissant : « book » (100 récupérables) en tête.
        summaries[0].InstanceCount.Should().Be(2);
    }

    [Fact]
    public async Task DeleteOrphans_RemovesUnreferencedLogicalFiles()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var referencedId = LogicalFileId.New();
        var orphanId = LogicalFileId.New();
        var now = DateTime.UtcNow;
        await logicalFiles.UpsertAsync(new LogicalFile(referencedId, MediaCategory.Unknown,
            ContentSignature.NameSize(CanonicalName.From("a.pdf"), 10), now, now), CancellationToken.None);
        await logicalFiles.UpsertAsync(new LogicalFile(orphanId, MediaCategory.Unknown,
            ContentSignature.NameSize(CanonicalName.From("orphan.pdf"), 99), now, now), CancellationToken.None);
        await instances.UpsertAsync(
            Make(@"C:\a.pdf", "a.pdf", 10) with { LogicalFileId = referencedId }, CancellationToken.None);

        await logicalFiles.DeleteOrphansAsync(CancellationToken.None);

        (await logicalFiles.FindBySignatureAsync(
            ContentSignature.NameSize(CanonicalName.From("orphan.pdf"), 99), CancellationToken.None))
            .Should().BeNull();
        (await logicalFiles.FindBySignatureAsync(
            ContentSignature.NameSize(CanonicalName.From("a.pdf"), 10), CancellationToken.None))
            .Should().NotBeNull();
    }

    private static FileInstanceRecord Make(string absolutePath, string canonical, long size) => new(
        FileInstanceId.New(),
        VolumeId.Default,
        FilePath.From(absolutePath),
        CanonicalName.From(canonical),
        size,
        new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
}
