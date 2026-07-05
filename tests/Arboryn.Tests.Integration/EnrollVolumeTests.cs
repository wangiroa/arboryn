using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class EnrollVolumeTests
{
    [Fact]
    public async Task Enroll_NewVolume_CreatesRow_WritesMarker_AndIsNew()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (handler, identifier, volumes, _, _) = Build(db,
            new VolumeProbe(FilePath.From(@"E:\"), VolumeKind.External, "AABBCCDD", null, "BACKUP"));

        var result = await handler.ExecuteAsync(FilePath.From(@"E:\Music\x.mp3"));

        result.IsNewlyEnrolled.Should().BeTrue();
        result.MarkerWritten.Should().BeTrue();
        identifier.WriteCount.Should().Be(1);
        identifier.Marker!.Id.Should().Be(result.Volume.Id);

        var bySerial = await volumes.FindBySerialAsync("AABBCCDD", CancellationToken.None);
        bySerial.Should().NotBeNull();
        bySerial!.Name.Should().Be("BACKUP");      // dérivé du label faute de friendly name
        bySerial.Kind.Should().Be(VolumeKind.External);
        bySerial.Status.Should().Be(VolumeStatus.Online);
        bySerial.MountPoint.Should().Be(@"E:\");
    }

    [Fact]
    public async Task Enroll_KnownByMarker_RecognizesSameId_DoesNotRewrite()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (handler, identifier, _, _, _) = Build(db,
            new VolumeProbe(FilePath.From(@"F:\"), VolumeKind.External, "11112222", null, "MEDIA"));

        var first = await handler.ExecuteAsync(FilePath.From(@"F:\a.mp3"), "Mon disque", CancellationToken.None);
        var writesAfterFirst = identifier.WriteCount;

        // Deuxième passage : le marqueur posé est désormais lu → reconnaissance.
        var second = await handler.ExecuteAsync(FilePath.From(@"F:\a.mp3"));

        second.IsNewlyEnrolled.Should().BeFalse();
        second.Volume.Id.Should().Be(first.Volume.Id);
        identifier.WriteCount.Should().Be(writesAfterFirst); // id + nom inchangés → pas de réécriture
    }

    [Fact]
    public async Task Enroll_KnownBySerial_WhenMarkerMissing_Recognizes_AndRewritesMarker()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (handler, identifier, volumes, _, _) = Build(db,
            new VolumeProbe(FilePath.From(@"G:\"), VolumeKind.External, "SERIAL99", null, "X"));

        // Volume déjà connu en base par son VSN, mais marqueur absent (ex. DB d'un autre PC, disque effacé).
        var knownId = VolumeId.New();
        await volumes.UpsertAsync(
            new VolumeRecord(knownId, "Connu", VolumeKind.External, VolumeStatus.Offline) { Serial = "SERIAL99" },
            CancellationToken.None);
        identifier.Marker = null;

        var result = await handler.ExecuteAsync(FilePath.From(@"G:\file.pdf"));

        result.IsNewlyEnrolled.Should().BeFalse();
        result.Volume.Id.Should().Be(knownId);
        identifier.Marker.Should().NotBeNull();                 // marqueur reposé
        identifier.Marker!.Id.Should().Be(knownId);
    }

    [Fact]
    public async Task Enroll_MigratesDefaultInstancesUnderRoot()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (handler, _, _, instances, _) = Build(db,
            new VolumeProbe(FilePath.From(@"E:\"), VolumeKind.External, "MIGRATE01", null, "LIB"));

        await instances.UpsertAsync(DefaultInstance(@"E:\Books\a.epub"), CancellationToken.None);
        await instances.UpsertAsync(DefaultInstance(@"E:\Books\series\b.epub"), CancellationToken.None);
        await instances.UpsertAsync(DefaultInstance(@"C:\elsewhere\c.epub"), CancellationToken.None);

        var result = await handler.ExecuteAsync(FilePath.From(@"E:\Books\a.epub"));

        result.MigratedInstances.Should().Be(2);
        var onNew = await instances.GetActiveInstancesAsync(result.Volume.Id, null, CancellationToken.None);
        onNew.Should().HaveCount(2);
        var onDefault = await instances.GetActiveInstancesAsync(VolumeId.Default, null, CancellationToken.None);
        onDefault.Should().ContainSingle(i => i.Path.Value == @"C:\elsewhere\c.epub");
    }

    [Fact]
    public async Task Enroll_InternalVolume_StampsLocalMachine()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (handler, _, _, _, machines) = Build(db,
            new VolumeProbe(FilePath.From(@"C:\"), VolumeKind.Internal, "C0DE0001", null, "Windows"), "ALICE-PC");

        var result = await handler.ExecuteAsync(FilePath.From(@"C:\Users\a\x.pdf"));

        result.Volume.MachineId.Should().NotBeNull();
        var all = await machines.GetAllAsync(CancellationToken.None);
        all.Should().ContainSingle();
        all[0].Name.Should().Be("ALICE-PC");        // libellé par défaut = hostname
        all[0].Hostname.Should().Be("ALICE-PC");
        all[0].Id.Value.Should().Be(result.Volume.MachineId);
    }

    [Fact]
    public async Task Enroll_Nas_LeavesMachineNull_AndCreatesNoMachine()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (handler, _, _, _, machines) = Build(db,
            new VolumeProbe(FilePath.From(@"\\nas\media"), VolumeKind.Nas, null, @"\\nas\media", "media"), "ALICE-PC");

        var result = await handler.ExecuteAsync(FilePath.From(@"\\nas\media\film.mkv"));

        result.Volume.MachineId.Should().BeNull();           // NAS agnostique
        (await machines.GetAllAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Enroll_ExternalCarriedToSecondPc_KeepsOriginalMachine()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (handlerA, identifier, volumes, instances, machines) = Build(db,
            new VolumeProbe(FilePath.From(@"E:\"), VolumeKind.External, "CARRY01", null, "USB"), "PC-A");

        var first = await handlerA.ExecuteAsync(FilePath.From(@"E:\a.mp3"));
        first.Volume.MachineId.Should().NotBeNull();

        // Même disque rebranché sur un autre PC : reconnu par le marqueur, propriétaire conservé.
        var handlerB = new EnrollVolumeHandler(
            identifier, volumes, instances, machines, new FakeMachineProvider("PC-B"),
            NullLogger<EnrollVolumeHandler>.Instance);
        var second = await handlerB.ExecuteAsync(FilePath.From(@"E:\a.mp3"));

        second.IsNewlyEnrolled.Should().BeFalse();
        second.Volume.MachineId.Should().Be(first.Volume.MachineId);   // ne flippe pas vers PC-B
    }

    [Fact]
    public async Task Enroll_SecondVolumeSameHost_ReusesMachineRow()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (handler1, _, _, _, machines) = Build(db,
            new VolumeProbe(FilePath.From(@"C:\"), VolumeKind.Internal, "HOST1AAA", null, "Sys"), "HOST1");
        var (handler2, _, _, _, _) = Build(db,
            new VolumeProbe(FilePath.From(@"D:\"), VolumeKind.Internal, "HOST1BBB", null, "Data"), "HOST1");

        var r1 = await handler1.ExecuteAsync(FilePath.From(@"C:\x"));
        var r2 = await handler2.ExecuteAsync(FilePath.From(@"D:\y"));

        r2.Volume.MachineId.Should().Be(r1.Volume.MachineId);
        (await machines.GetAllAsync(CancellationToken.None)).Should().ContainSingle();
    }

    private static (EnrollVolumeHandler Handler, FakeIdentifier Identifier, SqliteVolumeRepository Volumes, SqliteFileInstanceRepository Instances, SqliteMachineRepository Machines)
        Build(TestDatabase db, VolumeProbe probe, string hostname = "PC-TEST")
    {
        var identifier = new FakeIdentifier(probe);
        var volumes = new SqliteVolumeRepository(db.Factory);
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var machines = new SqliteMachineRepository(db.Factory);
        var handler = new EnrollVolumeHandler(
            identifier, volumes, instances, machines, new FakeMachineProvider(hostname),
            NullLogger<EnrollVolumeHandler>.Instance);
        return (handler, identifier, volumes, instances, machines);
    }

    private static FileInstanceRecord DefaultInstance(string absolutePath) => new(
        FileInstanceId.New(),
        VolumeId.Default,
        FilePath.From(absolutePath),
        CanonicalName.From(Path.GetFileName(absolutePath)),
        100,
        DateTime.UtcNow);

    /// <summary>Identifieur en mémoire : probe figée + marqueur émulé (read/write).</summary>
    private sealed class FakeIdentifier : IVolumeIdentifier
    {
        private readonly VolumeProbe _probe;

        public FakeIdentifier(VolumeProbe probe) => _probe = probe;

        public VolumeMarker? Marker { get; set; }

        public int WriteCount { get; private set; }

        public bool Writable { get; set; } = true;

        public VolumeProbe Probe(FilePath pathOnVolume) => _probe;

        public VolumeMarker? ReadMarker(FilePath volumeRoot) => Marker;

        public bool WriteMarker(FilePath volumeRoot, VolumeMarker marker)
        {
            WriteCount++;
            if (Writable)
            {
                Marker = marker;
            }

            return Writable;
        }
    }

    /// <summary>Fournit un nom d'hôte figé (émule <c>Environment.MachineName</c>).</summary>
    private sealed class FakeMachineProvider : ILocalMachineProvider
    {
        public FakeMachineProvider(string hostname) => Hostname = hostname;

        public string Hostname { get; }
    }
}
