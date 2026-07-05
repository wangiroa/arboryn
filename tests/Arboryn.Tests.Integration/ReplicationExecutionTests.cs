using Arboryn.Application.Abstractions;
using Arboryn.Application.Replication;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.FileSystem;
using Arboryn.Infrastructure.Persistence;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

/// <summary>
/// Inc 10 — exécution du plan de réplication : copie inter-volume vérifiée, rename/move, delete
/// corbeille, différé des volumes hors-ligne et reprise, annulation d'un lot.
/// </summary>
public class ReplicationExecutionTests
{
    [Fact]
    public async Task Copy_ReplicatesFile_VerifiesHash_AndCreatesTargetInstance()
    {
        await using var ctx = await Context.CreateAsync();
        var nas = await ctx.AddVolume("NAS", VolumeStatus.Online);
        var usb = await ctx.AddVolume("USB", VolumeStatus.Online);
        var lf = await ctx.AddLogicalBook();
        ctx.WriteFile(nas, @"in\book.epub", "the-real-contents");
        var srcId = await ctx.AddInstance(nas, @"in\book.epub", lf, 17);

        var plan = Plan(new PlacementOperation(
            OperationKind.Copy, lf, srcId, nas, usb, @"in\book.epub", @"Livres\book.epub", 17));
        var result = await ctx.Handler().ExecuteAsync(plan);

        result.Copied.Should().Be(1);
        result.Failed.Should().Be(0);
        File.Exists(ctx.AbsolutePath(usb, @"Livres\book.epub")).Should().BeTrue();
        File.ReadAllText(ctx.AbsolutePath(usb, @"Livres\book.epub")).Should().Be("the-real-contents");
        (await ctx.ActiveInstanceCount(usb)).Should().Be(1);
    }

    [Fact]
    public async Task Copy_IntegrityFailure_RecyclesTarget_AndReportsFailure()
    {
        await using var ctx = await Context.CreateAsync(corruptCopies: true);
        var nas = await ctx.AddVolume("NAS", VolumeStatus.Online);
        var usb = await ctx.AddVolume("USB", VolumeStatus.Online);
        var lf = await ctx.AddLogicalBook();
        ctx.WriteFile(nas, @"in\book.epub", "genuine");
        var srcId = await ctx.AddInstance(nas, @"in\book.epub", lf, 7);

        var plan = Plan(new PlacementOperation(
            OperationKind.Copy, lf, srcId, nas, usb, @"in\book.epub", @"Livres\book.epub", 7));
        var result = await ctx.Handler().ExecuteAsync(plan);

        result.Copied.Should().Be(0);
        result.Failed.Should().Be(1);
        // La copie corrompue a été envoyée à la corbeille ; aucune instance cible n'est créée.
        ctx.Bin.Sent.Should().Contain(p => p.Value == ctx.AbsolutePath(usb, @"Livres\book.epub"));
        (await ctx.ActiveInstanceCount(usb)).Should().Be(0);
    }

    [Fact]
    public async Task Move_MovesFileOnDisk_AndUpdatesInstancePath()
    {
        await using var ctx = await Context.CreateAsync();
        var vol = await ctx.AddVolume("USB", VolumeStatus.Online);
        var lf = await ctx.AddLogicalBook();
        ctx.WriteFile(vol, @"incoming\raw.epub", "book");
        var id = await ctx.AddInstance(vol, @"incoming\raw.epub", lf, 4);

        var plan = Plan(new PlacementOperation(
            OperationKind.Move, lf, id, vol, vol, @"incoming\raw.epub", @"Livres\clean.epub", 4));
        var result = await ctx.Handler().ExecuteAsync(plan);

        result.Moved.Should().Be(1);
        File.Exists(ctx.AbsolutePath(vol, @"Livres\clean.epub")).Should().BeTrue();
        File.Exists(ctx.AbsolutePath(vol, @"incoming\raw.epub")).Should().BeFalse();
        (await ctx.InstancePath(id)).Should().Be(ctx.AbsolutePath(vol, @"Livres\clean.epub"));
    }

    [Fact]
    public async Task Delete_RecyclesFile_AndMarksInstanceDeleted()
    {
        await using var ctx = await Context.CreateAsync();
        var vol = await ctx.AddVolume("USB", VolumeStatus.Online);
        var lf = await ctx.AddLogicalBook();
        ctx.WriteFile(vol, @"films\movie.mkv", "video");
        var id = await ctx.AddInstance(vol, @"films\movie.mkv", lf, 5);

        var plan = Plan(new PlacementOperation(
            OperationKind.Delete, lf, id, vol, vol, @"films\movie.mkv", @"films\movie.mkv", 5));
        var result = await ctx.Handler().ExecuteAsync(plan);

        result.Deleted.Should().Be(1);
        File.Exists(ctx.AbsolutePath(vol, @"films\movie.mkv")).Should().BeFalse();
        (await ctx.ActiveInstanceCount(vol)).Should().Be(0);
    }

    [Fact]
    public async Task OfflineTarget_DefersAsPending_ThenResumeExecutesOnReconnect()
    {
        await using var ctx = await Context.CreateAsync();
        var nas = await ctx.AddVolume("NAS", VolumeStatus.Online);
        var usb = await ctx.AddVolume("USB", VolumeStatus.Offline);
        var lf = await ctx.AddLogicalBook();
        ctx.WriteFile(nas, @"in\book.epub", "content-x");
        var srcId = await ctx.AddInstance(nas, @"in\book.epub", lf, 9);

        var plan = Plan(new PlacementOperation(
            OperationKind.Copy, lf, srcId, nas, usb, @"in\book.epub", @"Livres\book.epub", 9));
        var result = await ctx.Handler().ExecuteAsync(plan);

        result.Pending.Should().Be(1);
        result.Copied.Should().Be(0);
        File.Exists(ctx.AbsolutePath(usb, @"Livres\book.epub")).Should().BeFalse();

        // L'USB est rebranché → reprise.
        await ctx.Volumes.SetStatusAsync(usb, VolumeStatus.Online, CancellationToken.None);
        var resume = await ctx.Resume().ExecuteAsync();

        resume.Resumed.Should().Be(1);
        File.Exists(ctx.AbsolutePath(usb, @"Livres\book.epub")).Should().BeTrue();
        (await ctx.ActiveInstanceCount(usb)).Should().Be(1);
    }

    [Fact]
    public async Task Undo_OfCopyBatch_RecyclesTarget_AndRemovesTargetInstance()
    {
        await using var ctx = await Context.CreateAsync();
        var nas = await ctx.AddVolume("NAS", VolumeStatus.Online);
        var usb = await ctx.AddVolume("USB", VolumeStatus.Online);
        var lf = await ctx.AddLogicalBook();
        ctx.WriteFile(nas, @"in\book.epub", "payload");
        var srcId = await ctx.AddInstance(nas, @"in\book.epub", lf, 7);

        var plan = Plan(new PlacementOperation(
            OperationKind.Copy, lf, srcId, nas, usb, @"in\book.epub", @"Livres\book.epub", 7));
        var exec = await ctx.Handler().ExecuteAsync(plan);
        File.Exists(ctx.AbsolutePath(usb, @"Livres\book.epub")).Should().BeTrue();

        var undo = await ctx.Undo().ExecuteAsync(exec.BatchId);

        undo.Undone.Should().Be(1);
        File.Exists(ctx.AbsolutePath(usb, @"Livres\book.epub")).Should().BeFalse();
        (await ctx.ActiveInstanceCount(usb)).Should().Be(0);
    }

    private static PlacementPlan Plan(params PlacementOperation[] operations)
        => new(operations, System.Array.Empty<PlacementConflict>(), new Dictionary<VolumeId, long>(), 0);

    /// <summary>Fixe l'environnement : base, volumes (répertoires temp), dépôts et handlers réels.</summary>
    private sealed class Context : IAsyncDisposable
    {
        private readonly TestDatabase _db;
        private readonly TempDir _temp;
        private readonly bool _corruptCopies;
        private readonly Dictionary<VolumeId, string> _roots = new();

        public SqliteVolumeRepository Volumes { get; }
        public SqliteFileInstanceRepository Instances { get; }
        public SqliteLogicalFileRepository LogicalFiles { get; }
        public SqliteOperationJournal Journal { get; }
        public MovingRecycleBin Bin { get; }

        private Context(TestDatabase db, TempDir temp, bool corruptCopies)
        {
            _db = db;
            _temp = temp;
            _corruptCopies = corruptCopies;
            Volumes = new SqliteVolumeRepository(db.Factory);
            Instances = new SqliteFileInstanceRepository(db.Factory);
            LogicalFiles = new SqliteLogicalFileRepository(db.Factory);
            Journal = new SqliteOperationJournal(db.Factory);
            Bin = new MovingRecycleBin(Path.Combine(temp.Path, "_bin"));
        }

        public static async Task<Context> CreateAsync(bool corruptCopies = false)
            => new(await TestDatabase.CreateAsync(), new TempDir(), corruptCopies);

        private ReplicationOperationExecutor Executor()
        {
            IFileMover mover = _corruptCopies ? new CorruptingMover() : new FileSystemMover();
            return new ReplicationOperationExecutor(
                mover, Bin, new Sha256FileHasher(), new FileScanner(NullLogger<FileScanner>.Instance),
                Instances, Instances, Journal, NullLogger<ReplicationOperationExecutor>.Instance);
        }

        public ExecuteReplicationPlanHandler Handler()
            => new(Volumes, Executor(), NullLogger<ExecuteReplicationPlanHandler>.Instance);

        public ResumePendingReplicationHandler Resume()
            => new(Journal, Volumes, Executor());

        public UndoReplicationBatchHandler Undo()
            => new(Journal, new FileSystemMover(), Bin, Instances, NullLogger<UndoReplicationBatchHandler>.Instance);

        public async Task<VolumeId> AddVolume(string name, VolumeStatus status)
        {
            var id = VolumeId.New();
            var root = Path.Combine(_temp.Path, name);
            Directory.CreateDirectory(root);
            _roots[id] = root;
            await Volumes.UpsertAsync(
                new VolumeRecord(id, name, VolumeKind.External, status) { MountPoint = root },
                CancellationToken.None);
            return id;
        }

        public async Task<LogicalFileId> AddLogicalBook()
        {
            var lf = new LogicalFile(
                LogicalFileId.New(), MediaCategory.Book,
                ContentSignature.NameSize(CanonicalName.From("book.epub"), 100),
                DateTime.UtcNow, DateTime.UtcNow);
            await LogicalFiles.UpsertAsync(lf, CancellationToken.None);
            return lf.Id;
        }

        public void WriteFile(VolumeId volume, string relative, string content)
        {
            var abs = AbsolutePath(volume, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
            File.WriteAllText(abs, content);
        }

        public async Task<FileInstanceId> AddInstance(VolumeId volume, string relative, LogicalFileId lf, long size)
        {
            var abs = AbsolutePath(volume, relative);
            var id = await Instances.UpsertAsync(
                new FileInstanceRecord(
                    FileInstanceId.New(), volume, FilePath.From(abs),
                    CanonicalName.From(Path.GetFileName(abs)), size, DateTime.UnixEpoch),
                CancellationToken.None);
            await Instances.SetLogicalFileAsync(id, lf, CancellationToken.None);
            return id;
        }

        public string AbsolutePath(VolumeId volume, string relative) => Path.Combine(_roots[volume], relative);

        public async Task<long> ActiveInstanceCount(VolumeId volume)
        {
            await using var connection = await _db.Factory.OpenAsync();
            return await connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM file_instances WHERE volume_id = @v AND status = 'active';",
                new { v = volume.Value });
        }

        public async Task<string?> InstancePath(FileInstanceId id)
        {
            await using var connection = await _db.Factory.OpenAsync();
            return await connection.ExecuteScalarAsync<string?>(
                "SELECT relative_path FROM file_instances WHERE id = @Id;", new { Id = id.Value });
        }

        public async ValueTask DisposeAsync()
        {
            _temp.Dispose();
            await _db.DisposeAsync();
        }
    }

    /// <summary>Corbeille factice qui déplace réellement le fichier (effet disque observable) et sait restaurer.</summary>
    private sealed class MovingRecycleBin : IRecycleBin
    {
        private readonly string _binDir;
        public List<FilePath> Sent { get; } = new();

        public MovingRecycleBin(string binDir)
        {
            _binDir = binDir;
            Directory.CreateDirectory(binDir);
        }

        public Task<FilePath?> SendToRecycleBinAsync(FilePath path, CancellationToken cancellationToken)
        {
            Sent.Add(path);
            var recycled = Path.Combine(_binDir, Guid.NewGuid().ToString("N") + "_" + path.FileName);
            File.Move(path.Value, recycled, overwrite: false);
            return Task.FromResult<FilePath?>(FilePath.From(recycled));
        }

        public Task<bool> RestoreAsync(FilePath recycledPath, FilePath originalPath, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(originalPath.Value)!);
            File.Move(recycledPath.Value, originalPath.Value, overwrite: false);
            return Task.FromResult(true);
        }
    }

    /// <summary>Mover dont la copie écrit un contenu différent (simule une copie corrompue).</summary>
    private sealed class CorruptingMover : IFileMover
    {
        private readonly FileSystemMover _real = new();

        public bool Exists(FilePath path) => _real.Exists(path);

        public Task MoveAsync(FilePath source, FilePath target, CancellationToken cancellationToken)
            => _real.MoveAsync(source, target, cancellationToken);

        public Task CopyAsync(FilePath source, FilePath target, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target.Value)!);
            File.WriteAllText(target.Value, "CORRUPTED-" + Guid.NewGuid());
            return Task.CompletedTask;
        }
    }
}
