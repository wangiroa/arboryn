using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.FileSystem;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class Sha256FileHasherTests
{
    [Fact]
    public async Task Compute_MatchesKnownSha256()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Arboryn-hash-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "abc");

        try
        {
            var hasher = new Sha256FileHasher();
            var hash = await hasher.ComputeAsync(FilePath.From(path), CancellationToken.None);

            // SHA-256("abc") connu.
            hash.Value.Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Compute_IdenticalContent_ProducesEqualHashes()
    {
        var a = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Arboryn-h1-{Guid.NewGuid():N}.bin");
        var b = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Arboryn-h2-{Guid.NewGuid():N}.bin");
        await File.WriteAllTextAsync(a, "même contenu");
        await File.WriteAllTextAsync(b, "même contenu");

        try
        {
            var hasher = new Sha256FileHasher();
            var ha = await hasher.ComputeAsync(FilePath.From(a), CancellationToken.None);
            var hb = await hasher.ComputeAsync(FilePath.From(b), CancellationToken.None);

            ha.Should().Be(hb);
        }
        finally
        {
            try { File.Delete(a); } catch (IOException) { }
            try { File.Delete(b); } catch (IOException) { }
        }
    }
}
