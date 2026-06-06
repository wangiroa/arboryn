using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.FileSystem;
using Arboryn.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

/// <summary>Inc 6 — write-back des métadonnées canoniques dans le fichier audio + annulation.</summary>
public class MetadataWriteBackTests
{
    [Fact]
    public async Task Writer_WritesTags_AndReturnsPreviousValues()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "book.mp3");
        await File.WriteAllBytesAsync(path, MinimalMp3());
        using (var file = TagLib.File.Create(path))
        {
            file.Tag.Title = "Ancien";
            file.Save();
        }

        var writer = new TagLibAudioMetadataWriter();
        var previous = await writer.WriteAsync(
            FilePath.From(path),
            new Dictionary<string, string?> { [MetadataKeys.Title] = "Fondation" },
            CancellationToken.None);

        previous[MetadataKeys.Title].Should().Be("Ancien");

        var reread = await new TagLibAudioMetadataReader().ReadAsync(FilePath.From(path), CancellationToken.None);
        reread[MetadataKeys.Title].Should().Be("Fondation");
    }

    [Fact]
    public async Task WriteBackThenUndo_RestoresOriginalTags()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        var path = Path.Combine(temp.Path, "book.mp3");
        await File.WriteAllBytesAsync(path, MinimalMp3());
        using (var file = TagLib.File.Create(path))
        {
            file.Tag.Title = "Ancien";
            file.Tag.Album = "Vieux Album";
            file.Tag.AlbumArtists = new[] { "Vieux" };
            file.Save();
        }

        var instances = new SqliteFileInstanceRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var journal = new SqliteOperationJournal(db.Factory);
        var writers = new IContentMetadataWriter[] { new TagLibAudioMetadataWriter() };

        var id = await instances.UpsertAsync(
            new FileInstanceRecord(FileInstanceId.New(), VolumeId.Default, FilePath.From(path),
                CanonicalName.From("book.mp3"), Size: 11, ModifiedAt: DateTime.UnixEpoch),
            CancellationToken.None);

        // Métadonnées canoniques voulues, différentes des tags actuels du fichier.
        var now = DateTime.UtcNow;
        await metadata.UpsertAsync(new MetadataEntry(id, MetadataKeys.Title, "Fondation", MetadataSources.User, 1.0, now), CancellationToken.None);
        await metadata.UpsertAsync(new MetadataEntry(id, MetadataKeys.Album, "Fondation", MetadataSources.User, 1.0, now), CancellationToken.None);
        await metadata.UpsertAsync(new MetadataEntry(id, MetadataKeys.AlbumArtist, "Asimov", MetadataSources.User, 1.0, now), CancellationToken.None);

        var reader = new TagLibAudioMetadataReader();

        // Write-back : les tags du fichier reflètent désormais les valeurs canoniques.
        var writeBack = new WriteBackMetadataHandler(
            writers, instances, metadata, journal, NullLogger<WriteBackMetadataHandler>.Instance);
        (await writeBack.ExecuteAsync(VolumeId.Default)).Written.Should().Be(1);

        var afterWrite = await reader.ReadAsync(FilePath.From(path), CancellationToken.None);
        afterWrite[MetadataKeys.Title].Should().Be("Fondation");
        afterWrite[MetadataKeys.Album].Should().Be("Fondation");
        afterWrite[MetadataKeys.AlbumArtist].Should().Be("Asimov");

        // Annulation : les tags d'origine sont restaurés.
        var undo = new UndoWriteBackMetadataHandler(
            journal, writers, NullLogger<UndoWriteBackMetadataHandler>.Instance);
        var undoResult = await undo.ExecuteAsync();
        undoResult.HadBatch.Should().BeTrue();
        undoResult.Restored.Should().Be(1);

        var afterUndo = await reader.ReadAsync(FilePath.From(path), CancellationToken.None);
        afterUndo[MetadataKeys.Title].Should().Be("Ancien");
        afterUndo[MetadataKeys.Album].Should().Be("Vieux Album");
        afterUndo[MetadataKeys.AlbumArtist].Should().Be("Vieux");
    }

    private static byte[] MinimalMp3()
    {
        var bytes = new List<byte>(capacity: 104 * 8);
        for (var i = 0; i < 8; i++)
        {
            bytes.Add(0xFF);
            bytes.Add(0xFB);
            bytes.Add(0x10);
            bytes.Add(0xC0);
            for (var j = 0; j < 100; j++)
            {
                bytes.Add(0);
            }
        }

        return bytes.ToArray();
    }
}
