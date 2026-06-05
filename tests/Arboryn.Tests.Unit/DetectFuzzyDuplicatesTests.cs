using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class DetectFuzzyDuplicatesTests
{
    [Fact]
    public void GroupFuzzy_GroupsSimilarNamesOfDifferentSizes()
    {
        var instances = new[]
        {
            Rec(@"C:\a\Hamlet.pdf", "hamlet.pdf", 100),
            Rec(@"C:\b\Hamlet_v2.pdf", "hamlet v2.pdf", 120),
            Rec(@"C:\c\Macbeth.pdf", "macbeth.pdf", 90),
        };

        var groups = DetectFuzzyDuplicatesHandler.GroupFuzzy(instances, threshold: 0.85);

        groups.Should().ContainSingle();
        groups[0].Kind.Should().Be(DuplicateGroupKind.FuzzyName);
        groups[0].Members.Should().HaveCount(2);
        groups[0].Members.Select(m => m.CanonicalName.Value)
            .Should().BeEquivalentTo("hamlet.pdf", "hamlet v2.pdf");
    }

    [Fact]
    public void GroupFuzzy_ExcludesPurelyExactGroups()
    {
        // Mêmes nom canonique + taille → relève de la détection exacte, pas floue.
        var instances = new[]
        {
            Rec(@"C:\a\book.epub", "book.epub", 100),
            Rec(@"C:\b\book.epub", "book.epub", 100),
        };

        DetectFuzzyDuplicatesHandler.GroupFuzzy(instances, threshold: 0.85).Should().BeEmpty();
    }

    [Fact]
    public void GroupFuzzy_IncludesMixedExactAndVariant()
    {
        var instances = new[]
        {
            Rec(@"C:\a\hamlet.pdf", "hamlet.pdf", 100),
            Rec(@"C:\b\hamlet.pdf", "hamlet.pdf", 100),       // copie exacte
            Rec(@"C:\c\hamlet_v2.pdf", "hamlet v2.pdf", 130), // variante
        };

        var groups = DetectFuzzyDuplicatesHandler.GroupFuzzy(instances, threshold: 0.85);

        groups.Should().ContainSingle();
        groups[0].Members.Should().HaveCount(3);
    }

    [Fact]
    public void GroupFuzzy_NoSimilarNames_ReturnsEmpty()
    {
        var instances = new[]
        {
            Rec(@"C:\a\hamlet.pdf", "hamlet.pdf", 100),
            Rec(@"C:\b\macbeth.pdf", "macbeth.pdf", 100),
        };

        DetectFuzzyDuplicatesHandler.GroupFuzzy(instances, threshold: 0.85).Should().BeEmpty();
    }

    private static FileInstanceRecord Rec(string absolutePath, string canonical, long size) => new(
        FileInstanceId.New(),
        VolumeId.Default,
        FilePath.From(absolutePath),
        new CanonicalName(canonical),
        size,
        DateTime.UtcNow);
}
