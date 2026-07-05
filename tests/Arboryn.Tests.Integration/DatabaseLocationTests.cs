using Arboryn.Infrastructure.Database;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

/// <summary>
/// Résolution de l'emplacement de la base (Inc 13, A2) — chaîne de précédence pure.
/// </summary>
public class DatabaseLocationTests
{
    private const string LocalAppData = @"C:\Users\x\AppData\Local";

    [Fact]
    public void Env_WinsOverEverything()
    {
        var path = DatabaseLocation.Resolve(LocalAppData, @"E:\arboryn\index.db", @"D:\pointer.db", @"F:\full.db", "Arboryn/index.db");
        path.Should().Be(@"E:\arboryn\index.db");
    }

    [Fact]
    public void Pointer_WinsOverConfig()
    {
        var path = DatabaseLocation.Resolve(LocalAppData, null, @"D:\shared\index.db", @"F:\full.db", "Arboryn/index.db");
        path.Should().Be(@"D:\shared\index.db");
    }

    [Fact]
    public void ConfigFullPath_WinsOverRelative()
    {
        var path = DatabaseLocation.Resolve(LocalAppData, null, null, @"F:\full.db", "Arboryn/index.db");
        path.Should().Be(@"F:\full.db");
    }

    [Fact]
    public void EmptyFullPath_IsIgnored_FallsThroughToRelative()
    {
        var path = DatabaseLocation.Resolve(LocalAppData, null, null, "   ", "Arboryn/index.db");
        path.Should().Be(Path.GetFullPath(Path.Combine(LocalAppData, "Arboryn/index.db")));
    }

    [Fact]
    public void RelativePath_IsJoinedToLocalAppData()
    {
        var path = DatabaseLocation.Resolve(LocalAppData, null, null, null, "Arboryn/index.db");
        path.Should().Be(Path.GetFullPath(Path.Combine(LocalAppData, "Arboryn/index.db")));
    }

    [Fact]
    public void AllEmpty_UsesDefaultUnderLocalAppData()
    {
        var path = DatabaseLocation.Resolve(LocalAppData, null, null, null, null);
        path.Should().Be(Path.Combine(LocalAppData, "Arboryn", "index.db"));
    }

    [Fact]
    public void Pointer_RoundTrips_ThroughWriteReadClear()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"arboryn-ptr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            DatabaseLocation.ReadPointer(dir).Should().BeNull();

            DatabaseLocation.WritePointer(dir, @"D:\shared\index.db");
            DatabaseLocation.ReadPointer(dir).Should().Be(@"D:\shared\index.db");

            DatabaseLocation.ClearPointer(dir);
            DatabaseLocation.ReadPointer(dir).Should().BeNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
