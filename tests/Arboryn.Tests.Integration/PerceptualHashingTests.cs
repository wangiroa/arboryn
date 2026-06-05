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

/// <summary>
/// Tests perceptuels (Inc 5). Les images d'échantillon sont des BMP 24 bits écrits à la
/// main : un dégradé lisse, sa variante « quantifiée » (analogue d'une recompression avec
/// perte) et un damier (contenu très différent). Le hasher réel (CoenM + ImageSharp)
/// les décode par contenu, indépendamment de l'extension.
/// </summary>
public class PerceptualHashingTests
{
    [Fact]
    public async Task Hasher_RecompressedImage_IsCloseToOriginal_DifferentImageIsFar()
    {
        using var temp = new TempDir();
        var originalPath = Path.Combine(temp.Path, "original.bmp");
        var recompressedPath = Path.Combine(temp.Path, "recompressed.bmp");
        var otherPath = Path.Combine(temp.Path, "other.bmp");

        await File.WriteAllBytesAsync(originalPath, Bmp24(64, 64, Gradient));
        await File.WriteAllBytesAsync(recompressedPath, Bmp24(64, 64, QuantizedGradient));
        await File.WriteAllBytesAsync(otherPath, Bmp24(64, 64, Checkerboard));

        var hasher = new ImageSharpPerceptualHasher();
        var hOriginal = await hasher.ComputeAsync(FilePath.From(originalPath), CancellationToken.None);
        var hRecompressed = await hasher.ComputeAsync(FilePath.From(recompressedPath), CancellationToken.None);
        var hOther = await hasher.ComputeAsync(FilePath.From(otherPath), CancellationToken.None);

        hOriginal.Should().NotBeNull();
        hRecompressed.Should().NotBeNull();
        hOther.Should().NotBeNull();

        // La variante « recompressée » conserve une empreinte très proche de l'original…
        hOriginal!.Value.HammingDistance(hRecompressed!.Value).Should()
            .BeLessThanOrEqualTo(DetectPerceptualDuplicatesHandler.DefaultMaxDistance);

        // …tandis qu'une image visuellement différente est loin.
        hOriginal.Value.HammingDistance(hOther!.Value).Should()
            .BeGreaterThan(DetectPerceptualDuplicatesHandler.DefaultMaxDistance);
    }

    [Fact]
    public async Task Hasher_NonImage_ReturnsNull()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "notanimage.bmp");
        await File.WriteAllTextAsync(path, "ceci n'est pas une image");

        var hasher = new ImageSharpPerceptualHasher();
        (await hasher.ComputeAsync(FilePath.From(path), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ScanComputeDetectPromote_RecompressedImages_ShareOneLogicalFile()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        temp.WriteBytes("vacances.bmp", Bmp24(64, 64, Gradient));
        temp.WriteBytes("vacances2.bmp", Bmp24(64, 64, QuantizedGradient));
        temp.WriteBytes("autre.bmp", Bmp24(64, 64, Checkerboard));
        temp.Write("notes.txt", "pas une image");

        var repository = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var extractor = new ExtractMetadataHandler(
            metadata, Array.Empty<IContentMetadataReader>(), NullLogger<ExtractMetadataHandler>.Instance);
        var scanHandler = new ScanDirectoryHandler(
            new FileScanner(NullLogger<FileScanner>.Instance),
            repository, logicalFiles, extractor, NullLogger<ScanDirectoryHandler>.Instance);

        await scanHandler.ExecuteAsync(FilePath.From(temp.Path), VolumeId.Default);

        // 1) Calcule les empreintes perceptuelles des images (3 images, le .txt est ignoré).
        var computer = new ComputePerceptualHashesHandler(
            repository, new ImageSharpPerceptualHasher(), NullLogger<ComputePerceptualHashesHandler>.Instance);
        var hashed = await computer.ExecuteAsync(VolumeId.Default);
        hashed.Should().Be(3);

        // 2) Détecte le groupe perceptuel : les deux variantes du même visuel.
        var detector = new DetectPerceptualDuplicatesHandler(repository);
        var groups = await detector.ExecuteAsync(VolumeId.Default);
        groups.Should().HaveCount(1);
        groups[0].Members.Select(m => m.CanonicalName.Value)
            .Should().BeEquivalentTo("vacances.bmp", "vacances2.bmp");

        // 3) Promotion : les deux variantes partagent un même LogicalFile à signature phash.
        var promoter = new PromotePerceptualHandler(repository, logicalFiles, repository);
        (await promoter.ExecuteAsync(VolumeId.Default)).Should().Be(1);

        await using var connection = await db.Factory.OpenAsync();
        var sharedLogicalIds = (await connection.QueryAsync<string>(
            "SELECT DISTINCT logical_file_id FROM file_instances WHERE relative_path IN (@A, @B);",
            new
            {
                A = Path.Combine(temp.Path, "vacances.bmp"),
                B = Path.Combine(temp.Path, "vacances2.bmp"),
            })).ToList();
        sharedLogicalIds.Should().HaveCount(1, "les deux variantes doivent pointer le même LogicalFile");

        var signatureKind = await connection.ExecuteScalarAsync<string>(
            "SELECT content_signature_kind FROM logical_files WHERE id = @Id;",
            new { Id = sharedLogicalIds.Single() });
        signatureKind.Should().Be("phash");
    }

    // -------------------------------------------------------------------------
    // Générateurs d'images déterministes (BMP 24 bits)
    // -------------------------------------------------------------------------

    /// <summary>Dégradé lisse basse fréquence : empreinte perceptuelle stable.</summary>
    private static (byte R, byte G, byte B) Gradient(int x, int y)
        => ((byte)(x * 4), (byte)(y * 4), (byte)((x + y) * 2));

    /// <summary>Variante « recompressée » : même dégradé à résolution réduite (blocs 2×2),
    /// analogue d'un redimensionnement/recompression. Contenu basse fréquence préservé,
    /// donc empreinte très proche de l'original.</summary>
    private static (byte R, byte G, byte B) QuantizedGradient(int x, int y)
        => Gradient(x & ~1, y & ~1);

    private static (byte R, byte G, byte B) Checkerboard(int x, int y)
        => ((x / 8) + (y / 8)) % 2 == 0 ? ((byte)0, (byte)0, (byte)0) : ((byte)255, (byte)255, (byte)255);

    /// <summary>Encode une image BMP 24 bits non compressée. <paramref name="pixel"/> renvoie (R,G,B) pour (x,y).</summary>
    private static byte[] Bmp24(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        var rowSize = ((width * 3) + 3) / 4 * 4;   // lignes alignées sur 4 octets
        var imageSize = rowSize * height;
        const int headerSize = 54;
        var bytes = new byte[headerSize + imageSize];

        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        WriteInt32(bytes, 2, headerSize + imageSize);   // taille fichier
        WriteInt32(bytes, 10, headerSize);              // offset des pixels
        WriteInt32(bytes, 14, 40);                      // taille BITMAPINFOHEADER
        WriteInt32(bytes, 18, width);
        WriteInt32(bytes, 22, height);
        bytes[26] = 1;                                  // plans
        bytes[28] = 24;                                 // bits par pixel
        WriteInt32(bytes, 38, 2835);                    // résolution X (≈72 dpi)
        WriteInt32(bytes, 42, 2835);                    // résolution Y

        for (var fileRow = 0; fileRow < height; fileRow++)
        {
            var y = height - 1 - fileRow;               // BMP : lignes du bas vers le haut
            var rowStart = headerSize + (fileRow * rowSize);
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y);
                var p = rowStart + (x * 3);
                bytes[p] = b;                            // ordre BGR
                bytes[p + 1] = g;
                bytes[p + 2] = r;
            }
        }

        return bytes;
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
