using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class FilePathTests
{
    [Theory]
    [InlineData(@"C:\foo\bar", @"C:\foo\bar")]
    [InlineData("C:/foo/bar", @"C:\foo\bar")]
    [InlineData(@"C:\foo\bar\", @"C:\foo\bar")]
    [InlineData(@"C:\", @"C:\")]
    public void From_NormalisesSeparatorsAndTrailingSlash(string input, string expected)
    {
        FilePath.From(input).Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"foo\bar")]
    [InlineData("rapport.pdf")]
    public void From_RejectsEmptyOrRelative(string input)
    {
        var act = () => FilePath.From(input);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FileNameAndExtension_AreExtracted()
    {
        var p = FilePath.From(@"C:\docs\rapport.pdf");
        p.FileName.Should().Be("rapport.pdf");
        p.Extension.Should().Be(".pdf");
    }

    [Fact]
    public void Combine_AppendsRelativePath()
    {
        var p = FilePath.From(@"C:\library");
        var combined = p.Combine(RelativePath.From(@"Livres\a.epub"));
        combined.Value.Should().Be(@"C:\library\Livres\a.epub");
    }

    [Theory]
    [InlineData(@"C:\foo", @"\\?\C:\foo")]
    [InlineData(@"\\nas\share\f", @"\\?\UNC\nas\share\f")]
    public void ToExtendedLengthPath_AddsPrefix(string input, string expected)
    {
        FilePath.From(input).ToExtendedLengthPath().Should().Be(expected);
    }
}
