using Arboryn.Infrastructure.FileSystem;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class FpcalcOutputParserTests
{
    [Fact]
    public void Parse_ReadsRawFingerprint_IncludingNegativeValues()
    {
        // Sortie typique de `fpcalc -raw -json` : entiers 32 bits signés.
        const string json = """
            { "duration": 12.34, "fingerprint": [0, 1, -1, 2147483647, -2147483648] }
            """;

        var fingerprint = FpcalcOutputParser.Parse(json);

        fingerprint.Should().NotBeNull();
        fingerprint!.SubFingerprints.Should().Equal(
            0u, 1u, 0xFFFFFFFFu, 0x7FFFFFFFu, 0x80000000u);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ \"duration\": 1.0 }")]
    [InlineData("{ \"fingerprint\": [] }")]
    public void Parse_ReturnsNull_WhenNoUsableFingerprint(string json)
    {
        FpcalcOutputParser.Parse(json).Should().BeNull();
    }
}
