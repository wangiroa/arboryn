using Arboryn.Domain.Matching;
using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class PerceptualHashBkTreeTests
{
    [Fact]
    public void Search_EmptyTree_ReturnsNothing()
    {
        var tree = new PerceptualHashBkTree();
        tree.Search(new PerceptualHash(0), 10).Should().BeEmpty();
    }

    [Fact]
    public void Search_FindsHashesWithinRadius_AndExcludesFarOnes()
    {
        var tree = new PerceptualHashBkTree();
        tree.Add(new PerceptualHash(0x0UL), 0);          // distance 0 from query
        tree.Add(new PerceptualHash(0x3UL), 1);          // distance 2
        tree.Add(new PerceptualHash(0xFFFF_0000UL), 2);  // distance 16

        var hits = tree.Search(new PerceptualHash(0x0UL), maxDistance: 5);

        hits.Should().BeEquivalentTo(new[] { 0, 1 });
    }

    [Fact]
    public void Add_IdenticalHashes_AreAllReturned()
    {
        var tree = new PerceptualHashBkTree();
        tree.Add(new PerceptualHash(0x42UL), 7);
        tree.Add(new PerceptualHash(0x42UL), 8);

        tree.Count.Should().Be(2);
        tree.Search(new PerceptualHash(0x42UL), 0).Should().BeEquivalentTo(new[] { 7, 8 });
    }

    [Fact]
    public void Search_LargeRadius_ReturnsEverything()
    {
        var tree = new PerceptualHashBkTree();
        for (var i = 0; i < 20; i++)
        {
            tree.Add(new PerceptualHash((ulong)(1 << i)), i);
        }

        tree.Search(new PerceptualHash(0), 64).Should().HaveCount(20);
    }
}
