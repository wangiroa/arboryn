using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class CanonicalNameTests
{
    [Theory]
    [InlineData("Mon Livre.pdf", "mon livre.pdf")]
    [InlineData("MON LIVRE.PDF", "mon livre.pdf")]
    [InlineData("Mon Livre (1).pdf", "mon livre.pdf")]
    [InlineData("Mon Livre - Copy.pdf", "mon livre.pdf")]
    [InlineData("Mon Livre - Copie 2.pdf", "mon livre.pdf")]
    [InlineData("Étude française.pdf", "etude francaise.pdf")]
    [InlineData("test_file.name.with.dots.txt", "test file name with dots.txt")]
    // Chiffres non-parenthésés en fin de nom : numéros de séquence ou années à PRÉSERVER.
    [InlineData("DSC_123.jpg", "dsc 123.jpg")]
    [InlineData("IMG_45.jpg", "img 45.jpg")]
    [InlineData("rapport 2020.pdf", "rapport 2020.pdf")]
    [InlineData("photo 100.jpg", "photo 100.jpg")]
    public void CanonicalName_NormalisesExpectedVariants(string input, string expected)
    {
        var canonical = CanonicalName.From(input);
        canonical.Value.Should().Be(expected);
    }

    [Fact]
    public void CanonicalName_HandlesEmptyInput()
    {
        var canonical = CanonicalName.From(string.Empty);
        canonical.Value.Should().BeEmpty();
    }
}
