using Arboryn.Application.Abstractions;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class OperationJournalTests
{
    [Fact]
    public async Task Append_Get_MarkUndone_RoundTrip()
    {
        await using var db = await TestDatabase.CreateAsync();

        // operations.file_instance_id a une FK vers file_instances : on sème l'instance.
        var repo = new SqliteFileInstanceRepository(db.Factory);
        var instance = new FileInstanceRecord(
            FileInstanceId.New(), VolumeId.Default, FilePath.From(@"C:\docs\f.txt"),
            CanonicalName.From("f.txt"), 10, DateTime.UtcNow);
        await repo.UpsertAsync(instance, CancellationToken.None);

        var journal = new SqliteOperationJournal(db.Factory);
        var batch = BatchId.New();
        var now = DateTime.UtcNow;
        var op = new Operation(
            OperationId.New(),
            batch,
            OperationKind.Delete,
            instance.Id,
            FilePath.From(@"C:\docs\f.txt"),
            FilePath.From(@"C:\$Recycle.Bin\S-1-5\$Rabc.txt"),
            OperationStatus.Completed,
            now,
            now);

        await journal.AppendAsync(op, CancellationToken.None);

        var lastBatch = await journal.GetLastUndoableDeleteBatchAsync(CancellationToken.None);
        lastBatch.Should().NotBeNull();
        lastBatch!.Value.Value.Should().Be(batch.Value);

        var ops = await journal.GetBatchAsync(batch, CancellationToken.None);
        ops.Should().ContainSingle();
        ops[0].Kind.Should().Be(OperationKind.Delete);
        ops[0].Status.Should().Be(OperationStatus.Completed);
        ops[0].OldPath!.Value.Value.Should().Be(@"C:\docs\f.txt");
        ops[0].NewPath!.Value.Value.Should().Be(@"C:\$Recycle.Bin\S-1-5\$Rabc.txt");

        await journal.MarkUndoneAsync(op.Id, DateTime.UtcNow, CancellationToken.None);

        // Une fois annulé, le lot n'est plus proposé à l'undo.
        (await journal.GetLastUndoableDeleteBatchAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetRecent_ReturnsNewestFirst_BoundedByLimit()
    {
        await using var db = await TestDatabase.CreateAsync();

        var repo = new SqliteFileInstanceRepository(db.Factory);
        var instance = new FileInstanceRecord(
            FileInstanceId.New(), VolumeId.Default, FilePath.From(@"C:\docs\f.txt"),
            CanonicalName.From("f.txt"), 10, DateTime.UtcNow);
        await repo.UpsertAsync(instance, CancellationToken.None);

        var journal = new SqliteOperationJournal(db.Factory);
        var t0 = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);

        async Task Append(string name, DateTime at) => await journal.AppendAsync(new Operation(
            OperationId.New(), BatchId.New(), OperationKind.Rename, instance.Id,
            FilePath.From(@"C:\docs\old.txt"), FilePath.From($@"C:\docs\{name}"),
            OperationStatus.Completed, at, at), CancellationToken.None);

        await Append("a.txt", t0);
        await Append("b.txt", t0.AddMinutes(1));
        await Append("c.txt", t0.AddMinutes(2));

        var recent = await journal.GetRecentAsync(2, CancellationToken.None);

        recent.Should().HaveCount(2);                               // borné par la limite
        recent[0].NewPath!.Value.Value.Should().EndWith("c.txt");   // le plus récent d'abord
        recent[1].NewPath!.Value.Value.Should().EndWith("b.txt");
    }
}
