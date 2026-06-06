using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class AudioFingerprintTests
{
    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        var subs = new uint[] { 0u, 1u, 0xFFFFFFFFu, 0x80000000u, 42u };
        var fingerprint = new AudioFingerprint(subs);

        var decoded = AudioFingerprint.Decode(fingerprint.Encode());

        decoded.SubFingerprints.Should().Equal(subs);
        decoded.Length.Should().Be(5);
    }

    [Fact]
    public void StableDigest_IsDeterministic_AndSensitiveToContent()
    {
        var a = new AudioFingerprint(new uint[] { 1, 2, 3 });
        var b = new AudioFingerprint(new uint[] { 1, 2, 3 });
        var c = new AudioFingerprint(new uint[] { 1, 2, 4 });

        a.StableDigest().Should().Be(b.StableDigest());
        a.StableDigest().Should().NotBe(c.StableDigest());
        a.StableDigest().Should().HaveLength(16);
    }

    [Fact]
    public void Decode_Rejects_Empty()
    {
        var act = () => AudioFingerprint.Decode("  ");
        act.Should().Throw<ArgumentException>();
    }
}
