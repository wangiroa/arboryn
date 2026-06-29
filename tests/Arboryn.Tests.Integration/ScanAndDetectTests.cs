using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.FileSystem;
using Arboryn.Infrastructure.Persistence;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class ScanAndDetectTests
{
    [Fact]
    public async Task Scan_ThenDetect_FindsExactNameDuplicatesAcrossFolders()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        // "Mon Livre.pdf" et "mon livre (1).pdf" → même nom canonique + même taille.
        temp.Write(@"folderA\Mon Livre.pdf", "same-content");
        temp.Write(@"folderB\mon livre (1).pdf", "same-content");
        temp.Write(@"folderC\Autre.pdf", "different-bytes");

        var repository = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var extractor = new Arboryn.Application.UseCases.ExtractMetadataHandler(
            metadata, new Arboryn.Application.Abstractions.IContentMetadataReader[] { new TagLibAudioMetadataReader() },
            NullLogger<Arboryn.Application.UseCases.ExtractMetadataHandler>.Instance);
        var scanner = new FileScanner(NullLogger<FileScanner>.Instance);
        var scanHandler = new ScanDirectoryHandler(scanner, repository, logicalFiles, new LogicalFileResolver(logicalFiles), extractor, NullLogger<ScanDirectoryHandler>.Instance);

        var result = await scanHandler.ExecuteAsync(FilePath.From(temp.Path), VolumeId.Default);
        result.FilesProcessed.Should().Be(3);

        var detectHandler = new DetectExactDuplicatesHandler(repository);
        var groups = await detectHandler.ExecuteAsync(VolumeId.Default);

        groups.Should().HaveCount(1);
        groups[0].Kind.Should().Be(DuplicateGroupKind.ExactName);
        groups[0].Members.Should().HaveCount(2);
        groups[0].IsActionable.Should().BeTrue();

        // Inc 3 : chaque FileInstance doit être rattachée à un LogicalFile,
        // et les deux copies « Mon Livre » doivent partager le même.
        await using var connection = await db.Factory.OpenAsync();
        var unattached = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM file_instances WHERE logical_file_id IS NULL;");
        unattached.Should().Be(0);

        var logicalCount = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM logical_files;");
        logicalCount.Should().Be(2, "deux signatures distinctes : (mon livre.pdf, 12) et (autre.pdf, 15)");
    }

    [Fact]
    public async Task Scan_ReportsProgress()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();
        temp.Write("a.txt", "1");
        temp.Write("b.txt", "2");

        var repository = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var extractor = new Arboryn.Application.UseCases.ExtractMetadataHandler(
            metadata, new Arboryn.Application.Abstractions.IContentMetadataReader[] { new TagLibAudioMetadataReader() },
            NullLogger<Arboryn.Application.UseCases.ExtractMetadataHandler>.Instance);
        var scanner = new FileScanner(NullLogger<FileScanner>.Instance);
        var scanHandler = new ScanDirectoryHandler(scanner, repository, logicalFiles, new LogicalFileResolver(logicalFiles), extractor, NullLogger<ScanDirectoryHandler>.Instance);

        var progress = new RecordingProgress();

        var result = await scanHandler.ExecuteAsync(FilePath.From(temp.Path), VolumeId.Default, progress);

        result.FilesProcessed.Should().Be(2);
        // Le rapport final est toujours émis avec le total.
        progress.Reports.Should().Contain(2);
    }

    /// <summary>
    /// <see cref="IProgress{T}"/> synchrone : contrairement à <see cref="Progress{T}"/>,
    /// les rapports sont appliqués immédiatement, ce qui rend le test déterministe.
    /// </summary>
    private sealed class RecordingProgress : IProgress<ScanProgress>
    {
        public List<int> Reports { get; } = new();

        public void Report(ScanProgress value) => Reports.Add(value.FilesProcessed);
    }
}
