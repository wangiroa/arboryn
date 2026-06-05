using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.FileSystem;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class FileScannerTests : IDisposable
{
    private readonly string _root;

    public FileScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"Arboryn-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ScanAsync_EnumeratesAllFilesWithRelativePathsAndSizes()
    {
        WriteFile("a.txt", "12345");                              // 5 octets
        WriteFile(Path.Combine("sub", "b.txt"), "ab");           // 2 octets
        WriteFile(Path.Combine("sub", "deep", "c.txt"), "xyz");  // 3 octets

        var results = await CollectAsync();

        results.Should().HaveCount(3);
        results.Select(r => r.Path.FileName).Should().BeEquivalentTo("a.txt", "b.txt", "c.txt");
        results.Single(r => r.Path.FileName == "a.txt").Size.Should().Be(5);
        results.Single(r => r.Path.FileName == "c.txt").Size.Should().Be(3);
        // Le chemin est absolu et situé sous la racine scannée.
        results.Should().OnlyContain(r => r.Path.Value.StartsWith(_root, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScanAsync_SkipsDotFoldersAndKnownSystemFolders()
    {
        WriteFile("keep.txt", "x");
        WriteFile(Path.Combine(".git", "config"), "x");
        WriteFile(Path.Combine("AppData", "Local", "app.dat"), "x");
        WriteFile(Path.Combine("node_modules", "pkg", "index.js"), "x");
        WriteFile(Path.Combine("normal", "doc.txt"), "x");

        var results = await CollectAsync();

        results.Select(r => r.Path.FileName)
            .Should().BeEquivalentTo("keep.txt", "doc.txt");
    }

    [Fact]
    public async Task ScanAsync_NonExistentRoot_YieldsNothing()
    {
        var missing = FilePath.From(Path.Combine(_root, "does-not-exist"));
        var results = await CollectAsync(missing);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_PreCancelledToken_Throws()
    {
        for (var i = 0; i < 50; i++)
        {
            WriteFile($"f{i}.txt", "x");
        }

        var scanner = new FileScanner(NullLogger<FileScanner>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () =>
        {
            await foreach (var _ in scanner.ScanAsync(FilePath.From(_root), VolumeId.Default, cts.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private async Task<List<ScannedFile>> CollectAsync(FilePath? root = null)
    {
        var scanner = new FileScanner(NullLogger<FileScanner>.Instance);
        var list = new List<ScannedFile>();
        await foreach (var file in scanner.ScanAsync(
            root ?? FilePath.From(_root), VolumeId.Default, CancellationToken.None))
        {
            list.Add(file);
        }
        return list;
    }

    private void WriteFile(string relative, string content)
    {
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Nettoyage best-effort d'un répertoire temporaire.
        }
    }
}
