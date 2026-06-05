using Arboryn.Domain.Metadata;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class IsbnTests
{
    [Theory]
    [InlineData("urn:isbn:9782070612888", "9782070612888")]
    [InlineData("ISBN 978-2-07-061288-8", "9782070612888")]
    [InlineData("978 2 07 061288 8", "9782070612888")]
    [InlineData("2-07-061288-X", "207061288X")]
    public void TryExtract_FindsAndNormalizes(string input, string expected)
    {
        Isbn.TryExtract(input, out var normalized).Should().BeTrue();
        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("aucun identifiant ici")]
    [InlineData("12345")]
    public void TryExtract_ReturnsFalse_WhenNoIsbn(string? input)
    {
        Isbn.TryExtract(input, out var normalized).Should().BeFalse();
        normalized.Should().BeEmpty();
    }
}
