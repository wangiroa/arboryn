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
/// Tests vidéo (Inc 5). ffmpeg n'étant pas requis ici, l'extraction de keyframes est
/// simulée par un <see cref="FakeKeyframeExtractor"/> fournissant des images BMP réelles ;
/// le hasher vidéo (pHash CoenM par frame + agrégation), le store et la détection sont réels.
/// </summary>
public class VideoPerceptualHashingTests
{
    [Fact]
    public async Task Hasher_SameFrames_ProduceSameAggregate_DifferentFramesDiffer()
    {
        var gradientVideo = Frames(TestImages.Gradient(), TestImages.Gradient(), TestImages.Gradient());
        var reencodedVideo = Frames(TestImages.GradientReduced(), TestImages.GradientReduced(), TestImages.GradientReduced());
        var otherVideo = Frames(TestImages.Checkerboard(), TestImages.Checkerboard(), TestImages.Checkerboard());

        var hGradient = await Hash(gradientVideo, "film.mkv");
        var hGradientAgain = await Hash(gradientVideo, "copie.mkv");
        var hReencoded = await Hash(reencodedVideo, "film-x265.mp4");
        var hOther = await Hash(otherVideo, "autre.mkv");

        hGradient.Should().NotBeNull();
        hGradient.Should().Be(hGradientAgain, "le même contenu donne la même empreinte agrégée");

        hGradient!.Value.HammingDistance(hReencoded!.Value).Should()
            .BeLessThanOrEqualTo(DetectPerceptualDuplicatesHandler.DefaultMaxDistance);
        hGradient.Value.HammingDistance(hOther!.Value).Should()
            .BeGreaterThan(DetectPerceptualDuplicatesHandler.DefaultMaxDistance);
    }

    [Fact]
    public async Task Hasher_NoFrames_ReturnsNull()
    {
        var hasher = new VideoPerceptualHasher(new FakeKeyframeExtractor(_ => Array.Empty<byte[]>()));
        (await hasher.ComputeAsync(FilePath.From(@"C:\videos\vide.mkv"), CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task ScanComputeDetectPromote_ReencodedVideos_ShareOneVideoLogicalFile()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        temp.Write("film.mkv", "x");
        temp.Write("film-x265.mp4", "y");
        temp.Write("autre.mkv", "z");
        temp.Write("notes.txt", "pas une vidéo");

        var repository = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var extractor = new ExtractMetadataHandler(
            metadata, Array.Empty<IContentMetadataReader>(), NullLogger<ExtractMetadataHandler>.Instance);
        var scanHandler = new ScanDirectoryHandler(
            new FileScanner(NullLogger<FileScanner>.Instance),
            repository, logicalFiles, new LogicalFileResolver(logicalFiles), extractor, NullLogger<ScanDirectoryHandler>.Instance);

        await scanHandler.ExecuteAsync(FilePath.From(temp.Path), VolumeId.Default);

        // Extraction simulée : « film » → dégradé (recompressé pour l'un), « autre » → damier.
        var fakeExtractor = new FakeKeyframeExtractor(path =>
            path.FileName.StartsWith("film", StringComparison.OrdinalIgnoreCase)
                ? (path.FileName.Contains("x265", StringComparison.OrdinalIgnoreCase)
                    ? Frames(TestImages.GradientReduced(), TestImages.GradientReduced())
                    : Frames(TestImages.Gradient(), TestImages.Gradient()))
                : Frames(TestImages.Checkerboard(), TestImages.Checkerboard()));

        var computer = new ComputePerceptualHashesHandler(
            repository,
            new IPerceptualHasher[] { new VideoPerceptualHasher(fakeExtractor) },
            NullLogger<ComputePerceptualHashesHandler>.Instance);
        (await computer.ExecuteAsync(VolumeId.Default)).Should().Be(3); // 3 vidéos, .txt ignoré

        var detector = new DetectPerceptualDuplicatesHandler(repository);
        var groups = await detector.ExecuteAsync(VolumeId.Default);
        groups.Should().HaveCount(1);
        groups[0].Members.Select(m => m.CanonicalName.Value)
            .Should().BeEquivalentTo("film.mkv", "film x265.mp4"); // le nom canonique remplace '-' par ' '

        var promoter = new PromotePerceptualHandler(repository, logicalFiles, repository);
        (await promoter.ExecuteAsync(VolumeId.Default)).Should().Be(1);

        await using var connection = await db.Factory.OpenAsync();
        var sharedLogicalIds = (await connection.QueryAsync<string>(
            "SELECT DISTINCT logical_file_id FROM file_instances WHERE relative_path IN (@A, @B);",
            new { A = Path.Combine(temp.Path, "film.mkv"), B = Path.Combine(temp.Path, "film-x265.mp4") })).ToList();
        sharedLogicalIds.Should().HaveCount(1);

        var row = await connection.QuerySingleAsync<(string Kind, string Category)>(
            "SELECT content_signature_kind AS Kind, category AS Category FROM logical_files WHERE id = @Id;",
            new { Id = sharedLogicalIds.Single() });
        row.Kind.Should().Be("phash");
        row.Category.Should().Be("video", "la catégorie est déduite des membres du groupe");
    }

    private static async Task<PerceptualHash?> Hash(IReadOnlyList<byte[]> frames, string fileName)
    {
        var hasher = new VideoPerceptualHasher(new FakeKeyframeExtractor(_ => frames));
        return await hasher.ComputeAsync(FilePath.From(@"C:\videos\" + fileName), CancellationToken.None);
    }

    private static IReadOnlyList<byte[]> Frames(params byte[][] frames) => frames;

    private sealed class FakeKeyframeExtractor : IVideoKeyframeExtractor
    {
        private readonly Func<FilePath, IReadOnlyList<byte[]>> _frames;

        public FakeKeyframeExtractor(Func<FilePath, IReadOnlyList<byte[]>> frames) => _frames = frames;

        public Task<IReadOnlyList<byte[]>> ExtractKeyframesAsync(
            FilePath videoPath, int maxFrames, CancellationToken cancellationToken)
            => Task.FromResult(_frames(videoPath));
    }
}
