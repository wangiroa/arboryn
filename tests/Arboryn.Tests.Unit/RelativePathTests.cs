using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class RelativePathTests
{
    [Theory]
    [InlineData(@"sub\file.txt", @"sub\file.txt")]
    [InlineData("sub/file.txt", @"sub\file.txt")]
    [InlineData(@"\sub\file.txt\", @"sub\file.txt")]
    [InlineData("file.txt", "file.txt")]
    public void From_NormalisesAndTrims(string input, string expected)
    {
        RelativePath.From(input).Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_RejectsEmpty(string input)
    {
        var act = () => RelativePath.From(input);
        act.Should().Throw<ArgumentException>();
    }
}
