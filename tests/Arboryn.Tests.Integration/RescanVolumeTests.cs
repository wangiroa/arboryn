using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.FileSystem;
using Arboryn.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class RescanVolumeTests
{
    [Fact]
    public async Task FirstScan_NoUsnBaseline_FullWalkIndexesAllFiles()
    {
        using var temp = new TempDir();
        temp.Write("a.txt", "aaa");
        temp.Write("sub/b.txt", "bbbb");
        await using var db = await TestDatabase.CreateAsync();
        var (handler, volume, instances, _) = await BuildAsync(db, new FakeUsn());

        var result = await handler.ExecuteAsync(FilePath.From(temp.Path), volume);

        result.UsedUsnJournal.Should().BeFalse();
        result.Processed.Should().Be(2);
        result.Missing.Should().Be(0);
        var active = await instances.GetActiveInstancesAsync(volume.Id, FilePath.From(temp.Path), CancellationToken.None);
        active.Should().HaveCount(2);
    }

    [Fact]
    public async Task Rescan_NoChanges_SkipsEverything()
    {
        using var temp = new TempDir();
        temp.Write("a.txt", "aaa");
        temp.Write("b.txt", "bbb");
        await using var db = await TestDatabase.CreateAsync();
        var (handler, volume, _, _) = await BuildAsync(db, new FakeUsn());

        await handler.ExecuteAsync(FilePath.From(temp.Path), volume);
        var second = await handler.ExecuteAsync(FilePath.From(temp.Path), volume);

        second.Processed.Should().Be(0);
        second.Missing.Should().Be(0);
    }

    [Fact]
    public async Task Rescan_FullWalk_DetectsNew_Modified_AndMissing()
    {
        using var temp = new TempDir();
        temp.Write("a.txt", "aaa");
        temp.Write("b.txt", "bbb");
        await using var db = await TestDatabase.CreateAsync();
        var (handler, volume, instances, _) = await BuildAsync(db, new FakeUsn());
        await handler.ExecuteAsync(FilePath.From(temp.Path), volume);

        temp.Write("a.txt", "aaaaaaaaa"); // taille modifiée
        temp.Write("c.txt", "ccc");       // nouveau
        File.Delete(System.IO.Path.Combine(temp.Path, "b.txt")); // disparu

        var result = await handler.ExecuteAsync(FilePath.From(temp.Path), volume);

        result.Processed.Should().Be(2); // a modifié + c nouveau
        result.Missing.Should().Be(1);   // b manquant
        var active = await instances.GetActiveInstancesAsync(volume.Id, FilePath.From(temp.Path), CancellationToken.None);
        active.Select(i => i.Path.FileName).Should().BeEquivalentTo("a.txt", "c.txt");
    }

    [Fact]
    public async Task Rescan_UsnFastPath_ProcessesOnlyReportedChanges_AndRecordsPosition()
    {
        using var temp = new TempDir();
        temp.Write("a.txt", "aaa");
        temp.Write("b.txt", "bbb");
        temp.Write("c.txt", "ccc");
        await using var db = await TestDatabase.CreateAsync();

        // Le premier scan (sans référence USN) est un parcours complet qui pose la position 100.
        var fake = new FakeUsn { CurrentPosition = 100 };
        var (handler, volume, instances, volumes) = await BuildAsync(db, fake);
        await handler.ExecuteAsync(FilePath.From(temp.Path), volume);

        // Modifie a, supprime b sur le disque ; le journal ne signale que ces deux chemins.
        temp.Write("a.txt", "aaaaaaaaa");
        File.Delete(System.IO.Path.Combine(temp.Path, "b.txt"));
        fake.ChangeSet = new UsnChangeSet(new[]
        {
            new UsnChange(FilePath.From(System.IO.Path.Combine(temp.Path, "a.txt")), Deleted: false),
            new UsnChange(FilePath.From(System.IO.Path.Combine(temp.Path, "b.txt")), Deleted: true),
        }, NextUsn: 999);

        // Recharge le volume pour qu'il porte la position USN (100) posée par le premier scan.
        var reloaded = await volumes.GetAsync(volume.Id, CancellationToken.None);
        var result = await handler.ExecuteAsync(FilePath.From(temp.Path), reloaded!);

        result.UsedUsnJournal.Should().BeTrue();
        result.Processed.Should().Be(1); // a uniquement
        result.Missing.Should().Be(1);   // b
        // c n'était pas dans le delta → toujours actif.
        var active = await instances.GetActiveInstancesAsync(volume.Id, FilePath.From(temp.Path), CancellationToken.None);
        active.Select(i => i.Path.FileName).Should().BeEquivalentTo("a.txt", "c.txt");
        // La nouvelle position USN est mémorisée.
        (await volumes.GetAsync(volume.Id, CancellationToken.None))!.LastUsn.Should().Be(999);
    }

    private static async Task<(RescanVolumeHandler Handler, VolumeRecord Volume, SqliteFileInstanceRepository Instances, SqliteVolumeRepository Volumes)>
        BuildAsync(TestDatabase db, FakeUsn usn)
    {
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var metadataRepo = new SqliteFileMetadataRepository(db.Factory);
        var volumes = new SqliteVolumeRepository(db.Factory);
        var extractor = new ExtractMetadataHandler(
            metadataRepo, Array.Empty<IContentMetadataReader>(), NullLogger<ExtractMetadataHandler>.Instance);
        var handler = new RescanVolumeHandler(
            new FileScanner(NullLogger<FileScanner>.Instance),
            instances,
            logicalFiles,
            new LogicalFileResolver(logicalFiles),
            extractor,
            volumes,
            usn,
            NullLogger<RescanVolumeHandler>.Instance);

        var id = VolumeId.New();
        await volumes.UpsertAsync(
            new VolumeRecord(id, "USB", VolumeKind.External, VolumeStatus.Online), CancellationToken.None);
        var volume = (await volumes.GetAsync(id, CancellationToken.None))!;
        return (handler, volume, instances, volumes);
    }

    /// <summary>Lecteur USN en mémoire : mime la sémantique réelle (pas de delta sans référence).</summary>
    private sealed class FakeUsn : IUsnJournalReader
    {
        public long? CurrentPosition { get; set; }

        public UsnChangeSet? ChangeSet { get; set; }

        public Task<UsnChangeSet?> TryReadChangesAsync(VolumeRecord volume, FilePath root, CancellationToken cancellationToken)
            => Task.FromResult(volume.LastUsn is null ? null : ChangeSet);

        public Task<long?> TryGetCurrentPositionAsync(VolumeRecord volume, CancellationToken cancellationToken)
            => Task.FromResult(CurrentPosition);
    }
}
