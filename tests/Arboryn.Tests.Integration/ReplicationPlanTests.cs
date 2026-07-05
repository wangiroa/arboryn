using Arboryn.Application.Abstractions;
using Arboryn.Application.Replication;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Replication;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Persistence;
using Arboryn.Infrastructure.Templates;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

/// <summary>
/// Inc 10 — assemblage du catalogue de réplication et génération de bout en bout du plan de
/// placement depuis la base (périmètres de volume + catalogue logique + taxonomie).
/// </summary>
public class ReplicationPlanTests
{
    [Fact]
    public async Task Assembler_ExtractsSubcategoryAndYear_ForScopeSubject()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var volumes = new SqliteVolumeRepository(db.Factory);

        var vol = VolumeId.New();
        await volumes.UpsertAsync(new VolumeRecord(vol, "PC", VolumeKind.Internal, VolumeStatus.Online) { MountPoint = @"C:\" }, CancellationToken.None);

        var lf = await LinkInstance(instances, logicalFiles, MediaCategory.OfficialDocument,
            vol, @"C:\in\facture.pdf", size: 4096);
        var now = DateTime.UtcNow;
        await metadata.UpsertAsync(new MetadataEntry(lf.InstanceId, "subcategory", "Investissements", MetadataSources.Triage, 1.0, now), CancellationToken.None);
        await metadata.UpsertAsync(new MetadataEntry(lf.InstanceId, MetadataKeys.Year, "2024", MetadataSources.Triage, 1.0, now), CancellationToken.None);

        var assembler = Assembler(db);
        var catalog = await assembler.BuildAsync(new Dictionary<VolumeId, string?> { [vol] = @"C:\" }, CancellationToken.None);

        catalog.Should().ContainSingle();
        var subject = catalog[0].Subject;
        subject.Category.Should().Be(MediaCategory.OfficialDocument);
        subject.Subcategory.Should().Be("Investissements");
        subject.Year.Should().Be(2024);
    }

    [Fact]
    public async Task Assembler_IgnoresInstancesOutsideParticipatingVolumes()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);

        // Instance rattachée au volume « default » : hors périmètre de réplication.
        await LinkInstance(instances, logicalFiles, MediaCategory.Book, VolumeId.Default, @"C:\in\x.epub", 10);

        var catalog = await Assembler(db).BuildAsync(
            new Dictionary<VolumeId, string?> { [VolumeId.New()] = @"E:\" }, CancellationToken.None);

        catalog.Should().BeEmpty();
    }

    [Fact]
    public async Task PlanHandler_PlansCrossVolumeCopy_ForBookMissingOnInScopeVolume()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var volumes = new SqliteVolumeRepository(db.Factory);
        var scopes = new SqliteReplicationScopeRepository(db.Factory);

        // NAS = tout ; USB = livres seulement.
        var nas = VolumeId.New();
        var usb = VolumeId.New();
        var nasScope = ScopeId.New();
        var usbScope = ScopeId.New();
        await scopes.UpsertAsync(new ReplicationScope(nasScope, "NAS", ScopeExpression.All), CancellationToken.None);
        await scopes.UpsertAsync(new ReplicationScope(usbScope, "USB", ScopeExpression.Categories(MediaCategory.Book)), CancellationToken.None);
        await volumes.UpsertAsync(new VolumeRecord(nas, "NAS", VolumeKind.Nas, VolumeStatus.Online) { ReplicationScopeId = nasScope.Value, MountPoint = @"N:\" }, CancellationToken.None);
        await volumes.UpsertAsync(new VolumeRecord(usb, "USB", VolumeKind.External, VolumeStatus.Online) { ReplicationScopeId = usbScope.Value, MountPoint = @"E:\" }, CancellationToken.None);

        // Un livre présent seulement sur le NAS (chemin non canonique), absent de l'USB.
        var lf = await LinkInstance(instances, logicalFiles, MediaCategory.Book, nas, @"N:\incoming\book.epub", 5000);
        var now = DateTime.UtcNow;
        await metadata.UpsertAsync(new MetadataEntry(lf.InstanceId, MetadataKeys.Author, "Tolkien", MetadataSources.User, 1.0, now), CancellationToken.None);
        await metadata.UpsertAsync(new MetadataEntry(lf.InstanceId, MetadataKeys.Title, "Bilbo", MetadataSources.User, 1.0, now), CancellationToken.None);

        var plan = await PlanHandler(db).ExecuteAsync(CancellationToken.None);

        plan.Conflicts.Should().BeEmpty();
        var copy = plan.Operations.Should().ContainSingle(o => o.Kind == OperationKind.Copy).Subject;
        copy.SourceVolumeId.Should().Be(nas);
        copy.TargetVolumeId.Should().Be(usb);
        copy.IsCrossVolume.Should().BeTrue();
        plan.SpaceDeltaByVolume[usb].Should().Be(5000);
        // Le livre sur le NAS n'est pas au chemin canonique → un rename/move y est aussi planifié.
        plan.Operations.Should().Contain(o =>
            o.SourceVolumeId == nas && (o.Kind == OperationKind.Move || o.Kind == OperationKind.Rename));
    }

    [Fact]
    public async Task PlanHandler_PlansDeletion_ForOutOfScopeSurplus()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var volumes = new SqliteVolumeRepository(db.Factory);
        var scopes = new SqliteReplicationScopeRepository(db.Factory);

        // USB ne réplique que les livres ; on y place une vidéo → surplus à supprimer.
        var usb = VolumeId.New();
        var usbScope = ScopeId.New();
        await scopes.UpsertAsync(new ReplicationScope(usbScope, "USB", ScopeExpression.Categories(MediaCategory.Book)), CancellationToken.None);
        await volumes.UpsertAsync(new VolumeRecord(usb, "USB", VolumeKind.External, VolumeStatus.Online) { ReplicationScopeId = usbScope.Value, MountPoint = @"E:\" }, CancellationToken.None);

        var lf = await LinkInstance(instances, logicalFiles, MediaCategory.Video, usb, @"E:\films\movie.mkv", 9000);

        var plan = await PlanHandler(db).ExecuteAsync(CancellationToken.None);

        var delete = plan.Operations.Should().ContainSingle(o => o.Kind == OperationKind.Delete).Subject;
        delete.InstanceId.Should().Be(lf.InstanceId);
        delete.SourceVolumeId.Should().Be(usb);
        plan.SpaceDeltaByVolume[usb].Should().Be(-9000);
    }

    [Fact]
    public async Task PlanHandler_WithNoEnrolledVolumes_ReturnsEmpty()
    {
        await using var db = await TestDatabase.CreateAsync();

        var plan = await PlanHandler(db).ExecuteAsync(CancellationToken.None);

        plan.Operations.Should().BeEmpty();
        plan.Conflicts.Should().BeEmpty();
    }

    private static BuildReplicationCatalogHandler Assembler(TestDatabase db)
        => new(
            new SqliteReplicationCatalogReader(db.Factory),
            new SqliteFileMetadataRepository(db.Factory),
            new SqliteTaxonomyRepository(db.Factory),
            new CanonicalPathResolver(new ScribanTemplateRenderer()));

    private static BuildReplicationPlanHandler PlanHandler(TestDatabase db)
        => new(
            new SqliteVolumeRepository(db.Factory),
            new SqliteReplicationScopeRepository(db.Factory),
            Assembler(db),
            new PlacementPlanCalculator());

    private static async Task<(LogicalFileId LogicalId, FileInstanceId InstanceId)> LinkInstance(
        SqliteFileInstanceRepository instances,
        SqliteLogicalFileRepository logicalFiles,
        MediaCategory category,
        VolumeId volumeId,
        string absolutePath,
        long size)
    {
        var fileName = System.IO.Path.GetFileName(absolutePath);
        var signature = ContentSignature.NameSize(CanonicalName.From(fileName), size);
        var lf = new LogicalFile(LogicalFileId.New(), category, signature, DateTime.UtcNow, DateTime.UtcNow);
        await logicalFiles.UpsertAsync(lf, CancellationToken.None);

        var instanceId = await instances.UpsertAsync(
            new FileInstanceRecord(
                FileInstanceId.New(), volumeId, FilePath.From(absolutePath),
                CanonicalName.From(fileName), size, DateTime.UnixEpoch),
            CancellationToken.None);
        await instances.SetLogicalFileAsync(instanceId, lf.Id, CancellationToken.None);

        return (lf.Id, instanceId);
    }
}
