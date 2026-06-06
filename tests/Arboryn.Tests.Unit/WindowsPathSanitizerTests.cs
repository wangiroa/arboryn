using Arboryn.Domain.Taxonomy;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class WindowsPathSanitizerTests
{
    [Theory]
    [InlineData("Normal Name", "Normal Name")]
    [InlineData("a:b*c?d", "a b c d")]                  // caractères interdits → espaces
    [InlineData("trailing dots...", "trailing dots")]   // points finaux retirés
    [InlineData("trailing space  ", "trailing space")]
    [InlineData("  ", "_")]                              // vide après nettoyage → placeholder
    [InlineData("double  space", "double space")]       // espaces multiples compressés
    public void SanitizeSegment_CleansInvalidCharacters(string input, string expected)
    {
        WindowsPathSanitizer.SanitizeSegment(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("COM1")]
    [InlineData("LPT9.txt")]
    public void SanitizeSegment_PrefixesReservedNames(string reserved)
    {
        WindowsPathSanitizer.SanitizeSegment(reserved).Should().StartWith("_");
    }

    [Fact]
    public void SanitizeRelativeDirectory_SplitsAndRejoinsWithBackslash()
    {
        WindowsPathSanitizer.SanitizeRelativeDirectory("Livres audio/Asimov/Fondation")
            .Should().Be(@"Livres audio\Asimov\Fondation");
    }

    [Fact]
    public void SanitizeRelativeDirectory_SanitizesEachSegment_AndDropsEmpties()
    {
        WindowsPathSanitizer.SanitizeRelativeDirectory("a:b//c?d/")
            .Should().Be(@"a b\c d");
    }

    [Fact]
    public void SanitizeFileName_RemovesPathSeparators()
    {
        WindowsPathSanitizer.SanitizeFileName(@"a/b\c.txt").Should().Be("a b c.txt");
    }

    [Fact]
    public void SanitizeSegment_CapsLength()
    {
        var huge = new string('x', 500);
        WindowsPathSanitizer.SanitizeSegment(huge).Length.Should().BeLessThanOrEqualTo(200);
    }
}
