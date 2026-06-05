using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.FileSystem;
using Arboryn.Infrastructure.Persistence;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class ScanAndExtractMetadataTests
{
    [Fact]
    public async Task Scan_ExtractsFilenameAndAudioTags_EndToEnd()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        // Crée un MP3 valide minimal puis y écrit des tags ID3 via TagLib.
        var mp3Path = Path.Combine(temp.Path, "Hamlet.2010.1080p.mp3");
        await File.WriteAllBytesAsync(mp3Path, MinimalMp3());
        using (var f = TagLib.File.Create(mp3Path))
        {
            f.Tag.Title = "Hamlet";
            f.Tag.Performers = new[] { "Shakespeare" };
            f.Tag.Album = "Tragedies";
            f.Tag.Year = 1601;
            f.Save();
        }
        // Un fichier non-audio à côté, pour la branche filename-only.
        temp.Write("Rapport (2023).pdf", "x");

        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var extractor = new ExtractMetadataHandler(
            metadata, new IContentMetadataReader[] { new TagLibAudioMetadataReader() },
            NullLogger<ExtractMetadataHandler>.Instance);
        var scanHandler = new ScanDirectoryHandler(
            new FileScanner(NullLogger<FileScanner>.Instance),
            instances, logicalFiles, extractor, NullLogger<ScanDirectoryHandler>.Instance);

        await scanHandler.ExecuteAsync(FilePath.From(temp.Path), VolumeId.Default);

        // Récupère les ids réels persistés.
        await using var connection = await db.Factory.OpenAsync();
        var mp3Id = await connection.ExecuteScalarAsync<string>(
            "SELECT id FROM file_instances WHERE relative_path = @P;",
            new { P = mp3Path });
        var pdfId = await connection.ExecuteScalarAsync<string>(
            "SELECT id FROM file_instances WHERE relative_path = @P;",
            new { P = Path.Combine(temp.Path, "Rapport (2023).pdf") });

        // Le MP3 doit avoir des entrées ID3 (haute confiance) + filename (basse confiance).
        var mp3Fused = await metadata.GetFusedAsync(new FileInstanceId(mp3Id!), CancellationToken.None);
        mp3Fused[MetadataKeys.Title].Value.Should().Be("Hamlet");
        mp3Fused[MetadataKeys.Title].Source.Should().Be(MetadataSources.Id3);
        mp3Fused[MetadataKeys.Artist].Value.Should().Be("Shakespeare");
        mp3Fused[MetadataKeys.Album].Value.Should().Be("Tragedies");

        // Le PDF n'a que des entrées filename (cleanup).
        var pdfFused = await metadata.GetFusedAsync(new FileInstanceId(pdfId!), CancellationToken.None);
        pdfFused[MetadataKeys.Title].Value.Should().Be("Rapport");
        pdfFused[MetadataKeys.Title].Source.Should().Be(MetadataSources.Filename);
        pdfFused[MetadataKeys.Year].Value.Should().Be("2023");

        // La catégorie préliminaire (déduite de l'extension) est portée par le LogicalFile.
        var mp3Category = await connection.ExecuteScalarAsync<string>(
            "SELECT lf.category FROM logical_files lf JOIN file_instances fi ON fi.logical_file_id = lf.id WHERE fi.id = @P;",
            new { P = mp3Id });
        mp3Category.Should().Be("audiobook");

        var pdfCategory = await connection.ExecuteScalarAsync<string>(
            "SELECT lf.category FROM logical_files lf JOIN file_instances fi ON fi.logical_file_id = lf.id WHERE fi.id = @P;",
            new { P = pdfId });
        pdfCategory.Should().Be("other_document");
    }

    private static byte[] MinimalMp3()
    {
        var bytes = new List<byte>(capacity: 104 * 8);
        for (var i = 0; i < 8; i++)
        {
            bytes.Add(0xFF); bytes.Add(0xFB); bytes.Add(0x10); bytes.Add(0xC0);
            for (var j = 0; j < 100; j++)
            {
                bytes.Add(0);
            }
        }
        return bytes.ToArray();
    }
}
