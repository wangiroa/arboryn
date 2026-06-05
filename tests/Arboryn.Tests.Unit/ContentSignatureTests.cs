using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class ContentSignatureTests
{
    [Fact]
    public void NameSize_FormatsCanonicalAndSize()
    {
        var signature = ContentSignature.NameSize(CanonicalName.From("Mon Livre.pdf"), 12345);

        signature.Kind.Should().Be(ContentSignatureKind.NameSize);
        signature.Value.Should().Be("mon livre.pdf|12345");
    }

    [Fact]
    public void FromSha256_StoresHexAsValue()
    {
        var hash = Sha256.FromHex(new string('a', 64));

        var signature = ContentSignature.FromSha256(hash);

        signature.Kind.Should().Be(ContentSignatureKind.Sha256);
        signature.Value.Should().Be(hash.Value);
    }

    [Fact]
    public void SignaturesAreValueEqual()
    {
        var a = ContentSignature.NameSize(CanonicalName.From("a.txt"), 10);
        var b = ContentSignature.NameSize(CanonicalName.From("a.txt"), 10);
        var c = ContentSignature.NameSize(CanonicalName.From("a.txt"), 11);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }
}
