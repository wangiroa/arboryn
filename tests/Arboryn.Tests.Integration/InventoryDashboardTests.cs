using Arboryn.Application.Abstractions;
using Arboryn.Application.Inventory;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Replication;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

/// <summary>
/// Inc 11 — tableau de bord inventaire : matrice présent/en-scope/manque/surplus, synthèse
/// par catégorie, compteurs globaux, santé, et recherche cross-volume « où est X ? ».
/// </summary>
public class InventoryDashboardTests
{
    [Fact]
    public async Task Dashboard_ComputesPresenceGapAndSurplus_PerVolumeScope()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var volumes = new SqliteVolumeRepository(db.Factory);
        var scopes = new SqliteReplicationScopeRepository(db.Factory);

        var nas = await AddVolume(volumes, scopes, "NAS", VolumeStatus.Online, ScopeExpression.All);
        var usb = await AddVolume(volumes, scopes, "USB", VolumeStatus.Online, ScopeExpression.Categories(MediaCategory.Book));

        // Book1 sur NAS + USB (redondance) ; Book2 sur NAS seul ; Video1 sur NAS + USB.
        var book1 = await AddLogical(logicalFiles, MediaCategory.Book);
        var book2 = await AddLogical(logicalFiles, MediaCategory.Book);
        var video1 = await AddLogical(logicalFiles, MediaCategory.Video);
        await AddInstance(instances, nas, @"N:\books\book1.epub", book1, 100);
        await AddInstance(instances, usb, @"U:\books\book1.epub", book1, 100);
        await AddInstance(instances, nas, @"N:\books\book2.epub", book2, 200);
        await AddInstance(instances, nas, @"N:\films\video1.mkv", video1, 900);
        await AddInstance(instances, usb, @"U:\films\video1.mkv", video1, 900);

        var snapshot = await Handler(db).ExecuteAsync(CancellationToken.None);

        snapshot.Global.LogicalFiles.Should().Be(3);
        snapshot.Global.FileInstances.Should().Be(5);
        snapshot.Global.RedundancyRatio.Should().BeApproximately(5d / 3d, 0.001);

        var usbInv = snapshot.Volumes.Single(v => v.Name == "USB");
        // USB réplique les livres : Book2 (absent) = manque ; Video1 (présent hors scope) = surplus.
        usbInv.GapCount.Should().Be(1);
        usbInv.SurplusCount.Should().Be(1);
        var usbBookCell = usbInv.Cells.Single(c => c.Category == MediaCategory.Book);
        usbBookCell.Present.Should().Be(1);
        usbBookCell.InScope.Should().Be(2);
        usbBookCell.Gap.Should().Be(1);
        var usbVideoCell = usbInv.Cells.Single(c => c.Category == MediaCategory.Video);
        usbVideoCell.Surplus.Should().Be(1);
        usbVideoCell.InScope.Should().Be(0);

        var nasInv = snapshot.Volumes.Single(v => v.Name == "NAS");
        // NAS = tout : rien en surplus ; tout ce qui existe y est présent → aucun manque.
        nasInv.SurplusCount.Should().Be(0);
        nasInv.GapCount.Should().Be(0);
    }

    [Fact]
    public async Task Dashboard_Health_CountsOfflineAndStaleVolumes_AndPendingOps()
    {
        await using var db = await TestDatabase.CreateAsync();
        var volumes = new SqliteVolumeRepository(db.Factory);
        var scopes = new SqliteReplicationScopeRepository(db.Factory);
        var journal = new SqliteOperationJournal(db.Factory);
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);

        var nas = await AddVolume(volumes, scopes, "NAS", VolumeStatus.Online, ScopeExpression.All);
        var old = await AddVolume(volumes, scopes, "OLD", VolumeStatus.Offline, ScopeExpression.All);
        await volumes.RecordScanAsync(old, DateTime.UtcNow.AddDays(-60), null, CancellationToken.None);

        // Une opération de réplication en attente (volume hors-ligne), référençant une instance réelle.
        var book = await AddLogical(logicalFiles, MediaCategory.Book);
        var instanceId = await AddInstance(instances, nas, @"N:\books\b.epub", book, 10);
        await journal.AppendAsync(new Operation(
            OperationId.New(), BatchId.New(), OperationKind.Copy, instanceId,
            null, null, OperationStatus.Pending, DateTime.UtcNow,
            SourceVolumeId: old, TargetVolumeId: old), CancellationToken.None);

        var snapshot = await Handler(db).ExecuteAsync(CancellationToken.None);

        snapshot.Health.OfflineVolumes.Should().Be(1);
        snapshot.Health.StaleVolumes.Should().Be(1);
        snapshot.Health.PendingOperations.Should().Be(1);
        snapshot.Health.OldestScan.Should().NotBeNull();
    }

    [Fact]
    public async Task Search_GroupsByLogicalFile_AndListsVolumes()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var volumes = new SqliteVolumeRepository(db.Factory);
        var scopes = new SqliteReplicationScopeRepository(db.Factory);

        var nas = await AddVolume(volumes, scopes, "NAS", VolumeStatus.Online, ScopeExpression.All);
        var usb = await AddVolume(volumes, scopes, "USB", VolumeStatus.Online, ScopeExpression.All);
        var book1 = await AddLogical(logicalFiles, MediaCategory.Book);
        var book2 = await AddLogical(logicalFiles, MediaCategory.Book);
        var video1 = await AddLogical(logicalFiles, MediaCategory.Video);
        await AddInstance(instances, nas, @"N:\books\bilbo.epub", book1, 10);
        await AddInstance(instances, usb, @"U:\books\bilbo.epub", book1, 10);
        await AddInstance(instances, nas, @"N:\books\dune.epub", book2, 10);
        await AddInstance(instances, nas, @"N:\films\movie.mkv", video1, 10);

        var search = new CrossVolumeSearchHandler(new SqliteInventoryReader(db.Factory));

        var results = await search.SearchAsync("bilbo", cancellationToken: CancellationToken.None);
        results.Should().ContainSingle();
        results[0].LogicalFileId.Should().Be(book1);
        results[0].VolumeNames.Should().BeEquivalentTo(new[] { "NAS", "USB" });

        // Recherche partielle sur l'extension commune : deux œuvres livres.
        var epubs = await search.SearchAsync(".epub", cancellationToken: CancellationToken.None);
        epubs.Select(r => r.LogicalFileId).Should().BeEquivalentTo(new[] { book1, book2 });
    }

    [Fact]
    public async Task DrillDown_ListsMissingWorks_AndSurplusInstances()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var volumes = new SqliteVolumeRepository(db.Factory);
        var scopes = new SqliteReplicationScopeRepository(db.Factory);

        var nas = await AddVolume(volumes, scopes, "NAS", VolumeStatus.Online, ScopeExpression.All);
        var usb = await AddVolume(volumes, scopes, "USB", VolumeStatus.Online, ScopeExpression.Categories(MediaCategory.Book));

        var book1 = await AddLogical(logicalFiles, MediaCategory.Book);
        var book2 = await AddLogical(logicalFiles, MediaCategory.Book);
        var video1 = await AddLogical(logicalFiles, MediaCategory.Video);
        await AddInstance(instances, nas, @"N:\books\bilbo.epub", book1, 10);
        await AddInstance(instances, usb, @"U:\books\bilbo.epub", book1, 10);
        await AddInstance(instances, nas, @"N:\books\dune.epub", book2, 10);   // absent d'USB → manque
        await AddInstance(instances, usb, @"U:\films\movie.mkv", video1, 10);  // hors scope USB → surplus

        var drill = new VolumeDrillDownHandler(
            new SqliteInventoryReader(db.Factory), new SqliteVolumeRepository(db.Factory), new SqliteReplicationScopeRepository(db.Factory));

        var detail = await drill.ExecuteAsync(usb, CancellationToken.None);

        detail.Missing.Should().ContainSingle().Which.Name.Should().Be("dune.epub");
        detail.Surplus.Should().ContainSingle().Which.Name.Should().Be("movie.mkv");
    }

    [Fact]
    public async Task Export_ProducesCsvAndJson()
    {
        await using var db = await TestDatabase.CreateAsync();
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var volumes = new SqliteVolumeRepository(db.Factory);
        var scopes = new SqliteReplicationScopeRepository(db.Factory);

        var usb = await AddVolume(volumes, scopes, "USB", VolumeStatus.Online, ScopeExpression.Categories(MediaCategory.Book));
        var book = await AddLogical(logicalFiles, MediaCategory.Book);
        await AddInstance(instances, usb, @"U:\books\b.epub", book, 100);

        var export = new InventoryExportHandler(Handler(db));
        var result = await export.BuildAsync(CancellationToken.None);

        result.Csv.Should().StartWith("Volume;Statut;Catégorie;Présent;EnScope;Manque;Surplus");
        result.Csv.Should().Contain("USB;Online;Book;1;");
        // JSON valide et exploitable.
        using var doc = System.Text.Json.JsonDocument.Parse(result.Json);
        doc.RootElement.GetProperty("Global").GetProperty("LogicalFiles").GetInt64().Should().Be(1);
        doc.RootElement.GetProperty("Volumes").GetArrayLength().Should().Be(1);
    }

    private static InventoryDashboardHandler Handler(TestDatabase db)
        => new(
            new SqliteInventoryReader(db.Factory),
            new SqliteVolumeRepository(db.Factory),
            new SqliteReplicationScopeRepository(db.Factory),
            new SqliteOperationJournal(db.Factory));

    private static async Task<VolumeId> AddVolume(
        SqliteVolumeRepository volumes, SqliteReplicationScopeRepository scopes,
        string name, VolumeStatus status, ScopeExpression scope)
    {
        var scopeId = ScopeId.New();
        await scopes.UpsertAsync(new ReplicationScope(scopeId, name, scope), CancellationToken.None);
        var id = VolumeId.New();
        await volumes.UpsertAsync(
            new VolumeRecord(id, name, VolumeKind.External, status) { ReplicationScopeId = scopeId.Value },
            CancellationToken.None);
        return id;
    }

    private static async Task<LogicalFileId> AddLogical(SqliteLogicalFileRepository logicalFiles, MediaCategory category)
    {
        var lf = new LogicalFile(
            LogicalFileId.New(), category,
            ContentSignature.NameSize(CanonicalName.From(Guid.NewGuid().ToString("N")), 1),
            DateTime.UtcNow, DateTime.UtcNow);
        await logicalFiles.UpsertAsync(lf, CancellationToken.None);
        return lf.Id;
    }

    private static async Task<FileInstanceId> AddInstance(
        SqliteFileInstanceRepository instances, VolumeId volume, string absolutePath, LogicalFileId lf, long size)
    {
        var fileName = System.IO.Path.GetFileName(absolutePath);
        var id = await instances.UpsertAsync(
            new FileInstanceRecord(
                FileInstanceId.New(), volume, FilePath.From(absolutePath),
                CanonicalName.From(fileName), size, DateTime.UnixEpoch),
            CancellationToken.None);
        await instances.SetLogicalFileAsync(id, lf, CancellationToken.None);
        return id;
    }
}
