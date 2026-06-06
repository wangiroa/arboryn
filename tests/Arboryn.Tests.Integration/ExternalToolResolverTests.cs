using Arboryn.Infrastructure.FileSystem;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class ExternalToolResolverTests
{
    [Fact]
    public void Resolve_PrefersConfiguredPath_WhenItExists()
    {
        using var temp = new TempDir();
        var configured = Path.Combine(temp.Path, "custom-fpcalc.exe");
        File.WriteAllText(configured, "stub");

        var resolver = new ExternalToolResolver(temp.Path, pathProvider: () => null);

        resolver.Resolve("fpcalc.exe", configured).Should().Be(configured);
    }

    [Fact]
    public void Resolve_FindsBundledToolsFolder()
    {
        using var temp = new TempDir();
        var toolsDir = Path.Combine(temp.Path, "tools");
        Directory.CreateDirectory(toolsDir);
        var bundled = Path.Combine(toolsDir, "fpcalc.exe");
        File.WriteAllText(bundled, "stub");

        var resolver = new ExternalToolResolver(temp.Path, pathProvider: () => null);

        resolver.Resolve("fpcalc.exe").Should().Be(bundled);
    }

    [Fact]
    public void Resolve_SearchesPath()
    {
        using var temp = new TempDir();
        var pathDir = Path.Combine(temp.Path, "bin");
        Directory.CreateDirectory(pathDir);
        var onPath = Path.Combine(pathDir, "fpcalc.exe");
        File.WriteAllText(onPath, "stub");

        var resolver = new ExternalToolResolver(temp.Path, pathProvider: () => pathDir);

        resolver.Resolve("fpcalc.exe").Should().Be(onPath);
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenNotFoundAnywhere()
    {
        using var temp = new TempDir();
        var resolver = new ExternalToolResolver(temp.Path, pathProvider: () => null);

        resolver.Resolve("fpcalc.exe", configuredPath: @"C:\does\not\exist.exe").Should().BeNull();
    }
}
