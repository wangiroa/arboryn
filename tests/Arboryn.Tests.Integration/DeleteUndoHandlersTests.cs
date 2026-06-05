using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class DeleteUndoHandlersTests
{
    [Fact]
    public async Task Delete_RecyclesMarksDeletedAndJournals()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteFileInstanceRepository(db.Factory);
        var journal = new SqliteOperationJournal(db.Factory);
        var bin = new FakeRecycleBin();

        var keep = MakeRecord(@"C:\x\book.epub", "book.epub", 100);
        var dup = MakeRecord(@"C:\y\book.epub", "book.epub", 100);
        await repo.UpsertAsync(keep, CancellationToken.None);
        await repo.UpsertAsync(dup, CancellationToken.None);

        var handler = new DeleteFilesHandler(bin, journal, repo, NullLogger<DeleteFilesHandler>.Instance);
        var result = await handler.ExecuteAsync(new[] { new FileToDelete(dup.Id, dup.Path) });

        result.Deleted.Should().Be(1);
        result.Failed.Should().Be(0);
        bin.Sent.Should().ContainSingle().Which.Value.Should().Be(@"C:\y\book.epub");

        // Le doublon supprimé n'est plus candidat : il ne reste qu'une instance active.
        var candidates = await repo.GetDuplicateCandidatesAsync(VolumeId.Default, CancellationToken.None);
        candidates.Should().BeEmpty();

        var batch = await journal.GetLastUndoableDeleteBatchAsync(CancellationToken.None);
        batch.Should().NotBeNull();
    }

    [Fact]
    public async Task Undo_RestoresReactivatesAndMarksUndone()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteFileInstanceRepository(db.Factory);
        var journal = new SqliteOperationJournal(db.Factory);
        var bin = new FakeRecycleBin();

        var keep = MakeRecord(@"C:\x\book.epub", "book.epub", 100);
        var dup = MakeRecord(@"C:\y\book.epub", "book.epub", 100);
        await repo.UpsertAsync(keep, CancellationToken.None);
        await repo.UpsertAsync(dup, CancellationToken.None);

        var delete = new DeleteFilesHandler(bin, journal, repo, NullLogger<DeleteFilesHandler>.Instance);
        await delete.ExecuteAsync(new[] { new FileToDelete(dup.Id, dup.Path) });

        var undo = new UndoLastBatchHandler(journal, bin, repo, NullLogger<UndoLastBatchHandler>.Instance);
        var result = await undo.ExecuteAsync();

        result.HadBatch.Should().BeTrue();
        result.Restored.Should().Be(1);
        bin.Restored.Should().ContainSingle();
        bin.Restored[0].Original.Value.Should().Be(@"C:\y\book.epub");

        // Instance réactivée → le groupe de doublons réapparaît.
        var candidates = await repo.GetDuplicateCandidatesAsync(VolumeId.Default, CancellationToken.None);
        candidates.Should().HaveCount(2);

        // Plus rien à annuler.
        (await journal.GetLastUndoableDeleteBatchAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Undo_WithNoBatch_ReportsNothingToUndo()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteFileInstanceRepository(db.Factory);
        var journal = new SqliteOperationJournal(db.Factory);

        var undo = new UndoLastBatchHandler(journal, new FakeRecycleBin(), repo, NullLogger<UndoLastBatchHandler>.Instance);
        var result = await undo.ExecuteAsync();

        result.HadBatch.Should().BeFalse();
        result.Restored.Should().Be(0);
    }

    private static FileInstanceRecord MakeRecord(string absolutePath, string canonical, long size) => new(
        FileInstanceId.New(),
        VolumeId.Default,
        FilePath.From(absolutePath),
        CanonicalName.From(canonical),
        size,
        DateTime.UtcNow);

    /// <summary>Corbeille factice : enregistre les appels, simule un chemin recyclé.</summary>
    private sealed class FakeRecycleBin : IRecycleBin
    {
        public List<FilePath> Sent { get; } = new();
        public List<(FilePath Recycled, FilePath Original)> Restored { get; } = new();

        public Task<FilePath?> SendToRecycleBinAsync(FilePath path, CancellationToken cancellationToken)
        {
            Sent.Add(path);
            return Task.FromResult<FilePath?>(FilePath.From(@"C:\$Recycle.Bin\fake\" + path.FileName));
        }

        public Task<bool> RestoreAsync(FilePath recycledPath, FilePath originalPath, CancellationToken cancellationToken)
        {
            Restored.Add((recycledPath, originalPath));
            return Task.FromResult(true);
        }
    }
}
