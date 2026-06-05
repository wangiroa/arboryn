using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class DetectPerceptualDuplicatesTests
{
    [Fact]
    public void Group_ClustersNearHashes_ExcludesFarAndSingletons()
    {
        var items = new[]
        {
            Hashed("base.jpg", 0x0UL),          // base
            Hashed("recompressed.jpg", 0x3UL),  // distance 2 → same group
            Hashed("autre.jpg", 0xFFFF_0000UL), // distance 16 → singleton, excluded
        };

        var groups = DetectPerceptualDuplicatesHandler.Group(items, maxDistance: 10);

        groups.Should().HaveCount(1);
        groups[0].Kind.Should().Be(DuplicateGroupKind.Perceptual);
        groups[0].Members.Select(m => m.CanonicalName.Value)
            .Should().BeEquivalentTo("base.jpg", "recompressed.jpg");
    }

    [Fact]
    public void GroupCore_RepresentativeIsMinimalHash()
    {
        var items = new[]
        {
            Hashed("x.jpg", 0x10UL),
            Hashed("y.jpg", 0x12UL),
            Hashed("z.jpg", 0x13UL),
        };

        var groups = DetectPerceptualDuplicatesHandler.GroupCore(items, maxDistance: 10);

        groups.Should().HaveCount(1);
        groups[0].Representative.Value.Should().Be(0x10UL);
        groups[0].Members.Should().HaveCount(3);
    }

    [Fact]
    public void Group_TransitiveChain_FormsSingleComponent()
    {
        // 0b000 ~ 0b001 ~ 0b011 : chaque maillon est à distance 1 de son voisin,
        // mais les extrémités (0 et 3) sont à distance 2 : seule la transitivité les relie.
        var items = new[]
        {
            Hashed("a.jpg", 0b000UL),
            Hashed("b.jpg", 0b001UL),
            Hashed("c.jpg", 0b011UL),
        };

        var groups = DetectPerceptualDuplicatesHandler.Group(items, maxDistance: 1);

        groups.Should().HaveCount(1);
        groups[0].Members.Should().HaveCount(3);
    }

    [Fact]
    public void Group_FewerThanTwo_ReturnsEmpty()
    {
        DetectPerceptualDuplicatesHandler.Group([Hashed("solo.jpg", 0x1UL)], 10).Should().BeEmpty();
        DetectPerceptualDuplicatesHandler.Group([], 10).Should().BeEmpty();
    }

    private static PerceptualHashedInstance Hashed(string name, ulong hash)
    {
        var record = new FileInstanceRecord(
            FileInstanceId.New(),
            VolumeId.Default,
            FilePath.From(@"C:\photos\" + name),
            CanonicalName.From(name),
            Size: 1000,
            ModifiedAt: DateTime.UnixEpoch);
        return new PerceptualHashedInstance(record, new PerceptualHash(hash));
    }
}
