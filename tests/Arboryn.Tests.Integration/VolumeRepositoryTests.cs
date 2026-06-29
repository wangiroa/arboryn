using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Persistence;
using Dapper;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class VolumeRepositoryTests
{
    [Fact]
    public async Task Upsert_Then_Get_RoundTripsAllFields()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteVolumeRepository(db.Factory);
        var id = VolumeId.New();
        var seen = new DateTime(2026, 6, 20, 10, 30, 15, DateTimeKind.Utc);
        var record = new VolumeRecord(id, "Disque USB Sauvegarde", VolumeKind.External, VolumeStatus.Online)
        {
            Serial = "A1B2C3D4",
            Label = "BACKUP",
            MountPoint = @"E:\",
            LastUsn = 4242,
            LastSeenAt = seen,
        };

        await repo.UpsertAsync(record, CancellationToken.None);
        var fetched = await repo.GetAsync(id, CancellationToken.None);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Disque USB Sauvegarde");
        fetched.Kind.Should().Be(VolumeKind.External);
        fetched.Status.Should().Be(VolumeStatus.Online);
        fetched.Serial.Should().Be("A1B2C3D4");
        fetched.Label.Should().Be("BACKUP");
        fetched.MountPoint.Should().Be(@"E:\");
        fetched.LastUsn.Should().Be(4242);
        fetched.LastSeenAt.Should().Be(seen);
    }

    [Fact]
    public async Task Upsert_OnExistingId_Updates()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteVolumeRepository(db.Factory);
        var id = VolumeId.New();
        await repo.UpsertAsync(new VolumeRecord(id, "Ancien", VolumeKind.External, VolumeStatus.Offline), CancellationToken.None);

        await repo.UpsertAsync(new VolumeRecord(id, "Nouveau", VolumeKind.Internal, VolumeStatus.Online), CancellationToken.None);

        await using var connection = await db.Factory.OpenAsync();
        var count = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM volumes WHERE id = @Id;", new { Id = id.Value });
        count.Should().Be(1);
        (await repo.GetAsync(id, CancellationToken.None))!.Name.Should().Be("Nouveau");
    }

    [Fact]
    public async Task FindBySerial_And_FindByFingerprint_Match()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteVolumeRepository(db.Factory);
        var ntfs = VolumeId.New();
        var nas = VolumeId.New();
        await repo.UpsertAsync(
            new VolumeRecord(ntfs, "USB", VolumeKind.External, VolumeStatus.Online) { Serial = "DEADBEEF" },
            CancellationToken.None);
        await repo.UpsertAsync(
            new VolumeRecord(nas, "NAS", VolumeKind.Nas, VolumeStatus.Online) { Fingerprint = @"\\nas\media" },
            CancellationToken.None);

        (await repo.FindBySerialAsync("DEADBEEF", CancellationToken.None))!.Id.Should().Be(ntfs);
        (await repo.FindByFingerprintAsync(@"\\nas\media", CancellationToken.None))!.Id.Should().Be(nas);
        (await repo.FindBySerialAsync("NOPE", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetAll_IncludesSeededDefaultVolume()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteVolumeRepository(db.Factory);

        var all = await repo.GetAllAsync(CancellationToken.None);

        all.Should().ContainSingle(v => v.Id == VolumeId.Default)
            .Which.Kind.Should().Be(VolumeKind.Default);
    }

    [Fact]
    public async Task SetStatus_UpdatesStatus()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteVolumeRepository(db.Factory);
        var id = VolumeId.New();
        await repo.UpsertAsync(new VolumeRecord(id, "USB", VolumeKind.External, VolumeStatus.Online), CancellationToken.None);

        await repo.SetStatusAsync(id, VolumeStatus.Offline, CancellationToken.None);

        (await repo.GetAsync(id, CancellationToken.None))!.Status.Should().Be(VolumeStatus.Offline);
    }

    [Fact]
    public async Task RecordScan_SetsScanTime_AndPreservesUsnWhenNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteVolumeRepository(db.Factory);
        var id = VolumeId.New();
        await repo.UpsertAsync(
            new VolumeRecord(id, "USB", VolumeKind.External, VolumeStatus.Online) { LastUsn = 100 },
            CancellationToken.None);

        // Scan NTFS : nouvelle position USN.
        await repo.RecordScanAsync(id, new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc), 555, CancellationToken.None);
        (await repo.GetAsync(id, CancellationToken.None))!.LastUsn.Should().Be(555);

        // Scan non-NTFS (USN null) : la position connue est préservée.
        await repo.RecordScanAsync(id, new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc), null, CancellationToken.None);
        var after = await repo.GetAsync(id, CancellationToken.None);
        after!.LastUsn.Should().Be(555);
        after.LastScanAt.Should().Be(new DateTime(2026, 6, 21, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ReassignDefaultUnderRoot_MovesOnlyMatchingPrefix()
    {
        await using var db = await TestDatabase.CreateAsync();
        var volumes = new SqliteVolumeRepository(db.Factory);
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var target = VolumeId.New();
        await volumes.UpsertAsync(new VolumeRecord(target, "USB", VolumeKind.External, VolumeStatus.Online), CancellationToken.None);

        await instances.UpsertAsync(Instance(@"E:\Music\a.mp3"), CancellationToken.None);
        await instances.UpsertAsync(Instance(@"E:\Music\sub\b.mp3"), CancellationToken.None);
        await instances.UpsertAsync(Instance(@"D:\other\c.mp3"), CancellationToken.None);

        var migrated = await instances.ReassignDefaultUnderRootAsync(
            FilePath.From(@"E:\"), target, CancellationToken.None);

        migrated.Should().Be(2);
        await using var connection = await db.Factory.OpenAsync();
        var onTarget = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM file_instances WHERE volume_id = @v;", new { v = target.Value });
        var onDefault = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM file_instances WHERE volume_id = @v;", new { v = VolumeId.Default.Value });
        onTarget.Should().Be(2);
        onDefault.Should().Be(1);
    }

    private static FileInstanceRecord Instance(string absolutePath) => new(
        FileInstanceId.New(),
        VolumeId.Default,
        FilePath.From(absolutePath),
        CanonicalName.From(Path.GetFileName(absolutePath)),
        100,
        DateTime.UtcNow);
}
