using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class PerceptualHashTests
{
    [Fact]
    public void ToHex_Produces16Chars_RoundTrips()
    {
        var hash = new PerceptualHash(0x00FF00FF00FF00FFUL);

        hash.ToHex().Should().Be("00ff00ff00ff00ff");
        PerceptualHash.FromHex(hash.ToHex()).Should().Be(hash);
    }

    [Fact]
    public void HammingDistance_IsZero_ForSameHash()
    {
        var hash = new PerceptualHash(0xDEADBEEFDEADBEEFUL);
        hash.HammingDistance(hash).Should().Be(0);
    }

    [Theory]
    [InlineData(0x0UL, 0x1UL, 1)]
    [InlineData(0x0UL, 0xFUL, 4)]
    [InlineData(0x0UL, 0xFFFFFFFFFFFFFFFFUL, 64)]
    [InlineData(0xF0F0UL, 0x0F0FUL, 16)]
    public void HammingDistance_CountsDifferingBits(ulong a, ulong b, int expected)
    {
        new PerceptualHash(a).HammingDistance(new PerceptualHash(b)).Should().Be(expected);
    }

    [Fact]
    public void FromHex_Rejects_Empty()
    {
        var act = () => PerceptualHash.FromHex("  ");
        act.Should().Throw<ArgumentException>();
    }
}
