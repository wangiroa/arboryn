using Arboryn.Domain.Matching;
using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class PerceptualHashAggregatorTests
{
    [Fact]
    public void MajorityVote_SingleHash_ReturnsItUnchanged()
    {
        var hash = new PerceptualHash(0xDEADBEEFDEADBEEFUL);
        PerceptualHashAggregator.MajorityVote(new[] { hash }).Should().Be(hash);
    }

    [Fact]
    public void MajorityVote_IdenticalHashes_ReturnThatHash()
    {
        var hash = new PerceptualHash(0xF0F0F0F0F0F0F0F0UL);
        PerceptualHashAggregator.MajorityVote(new[] { hash, hash, hash }).Should().Be(hash);
    }

    [Fact]
    public void MajorityVote_PicksMajorityBitPerPosition()
    {
        // bit0 : 1,1,0 → majorité 1 ; bit1 : 1,0,0 → majorité 0.
        var a = new PerceptualHash(0b11UL);
        var b = new PerceptualHash(0b01UL);
        var c = new PerceptualHash(0b00UL);

        PerceptualHashAggregator.MajorityVote(new[] { a, b, c }).Value.Should().Be(0b01UL);
    }

    [Fact]
    public void MajorityVote_Tie_LeavesBitZero()
    {
        // bit0 : 1,0 → égalité → 0 (déterministe).
        var a = new PerceptualHash(0b1UL);
        var b = new PerceptualHash(0b0UL);

        PerceptualHashAggregator.MajorityVote(new[] { a, b }).Value.Should().Be(0UL);
    }

    [Fact]
    public void MajorityVote_Empty_Throws()
    {
        var act = () => PerceptualHashAggregator.MajorityVote(Array.Empty<PerceptualHash>());
        act.Should().Throw<ArgumentException>();
    }
}
