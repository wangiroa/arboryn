using System;
using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class Sha256Tests
{
    [Fact]
    public void FromBytes_ProducesLowercaseHex()
    {
        var bytes = new byte[32];
        bytes[0] = 0xAB;

        var hash = Sha256.FromBytes(bytes);

        hash.Value.Should().HaveLength(64);
        hash.Value.Should().StartWith("ab");
        hash.Value.Should().Be(hash.Value.ToLowerInvariant());
    }

    [Fact]
    public void FromBytes_WrongLength_Throws()
    {
        var act = () => Sha256.FromBytes(new byte[16]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromHex_RoundTripsAndNormalisesCase()
    {
        var hex = new string('A', 64);
        Sha256.FromHex(hex).Value.Should().Be(new string('a', 64));
    }

    [Theory]
    [InlineData("tooshort")]
    [InlineData("")]
    public void FromHex_Invalid_Throws(string hex)
    {
        var act = () => Sha256.FromHex(hex);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromBytes_TwoEqualInputs_AreEqualValueObjects()
    {
        var a = Sha256.FromBytes(new byte[32]);
        var b = Sha256.FromBytes(new byte[32]);
        a.Should().Be(b);
    }
}
