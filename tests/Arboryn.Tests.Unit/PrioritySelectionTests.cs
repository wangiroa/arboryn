using Arboryn.Application.UseCases;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class PrioritySelectionTests
{
    [Fact]
    public void RankDirectories_KeepsOnlyThoseInAtLeastTwoGroups_OrderedByFrequency()
    {
        var groups = new IReadOnlyCollection<string>[]
        {
            new[] { @"C:\NAS", @"C:\PC" },
            new[] { @"C:\NAS", @"C:\USB" },
            new[] { @"C:\NAS", @"C:\PC" },
        };

        var ranked = PrioritySelection.RankDirectories(groups);

        // NAS dans 3 groupes, PC dans 2, USB dans 1 (exclu).
        ranked.Should().Equal(@"C:\NAS", @"C:\PC");
    }

    [Fact]
    public void RankDirectories_NoRecurringDirectory_ReturnsEmpty()
    {
        var groups = new IReadOnlyCollection<string>[]
        {
            new[] { @"C:\a" },
            new[] { @"C:\b" },
        };

        PrioritySelection.RankDirectories(groups).Should().BeEmpty();
    }

    [Fact]
    public void ChooseKeepIndex_PrefersEarlierPrefixInOrderedList()
    {
        var members = new[]
        {
            new KeepCandidate(@"C:\Users\X\Downloads", 100, "f.pdf"),
            new KeepCandidate(@"C:\Users\X\Documents\Projets", 100, "f.pdf"),
        };
        var priorities = new[] { @"C:\Users\X\Documents", @"C:\Users\X\Downloads" };

        // Documents passe avant Downloads → on garde l'index 1.
        PrioritySelection.ChooseKeepIndex(members, priorities, useScore: true).Should().Be(1);
    }

    [Fact]
    public void ChooseKeepIndex_TieOnPrefix_KeepsDeepestWhenScoring()
    {
        var members = new[]
        {
            new KeepCandidate(@"C:\Docs\A", 100, "f.pdf"),
            new KeepCandidate(@"C:\Docs\A\B\C", 100, "f.pdf"),
        };
        var priorities = new[] { @"C:\Docs" };

        // Même rang sous Docs → le score (profondeur dominante) garde le plus profond.
        PrioritySelection.ChooseKeepIndex(members, priorities, useScore: true).Should().Be(1);
    }

    [Fact]
    public void ChooseKeepIndex_NoPriority_UsesScoreOrFirst()
    {
        var members = new[]
        {
            new KeepCandidate(@"C:\a", 100, "f.pdf"),
            new KeepCandidate(@"C:\a\b\c", 100, "f.pdf"),
        };

        PrioritySelection.ChooseKeepIndex(members, System.Array.Empty<string>(), useScore: true).Should().Be(1);
        PrioritySelection.ChooseKeepIndex(members, System.Array.Empty<string>(), useScore: false).Should().Be(0);
    }

    [Fact]
    public void ChooseKeepIndex_PrefixMatchesOnDirectoryBoundary()
    {
        var members = new[]
        {
            new KeepCandidate(@"C:\Documents2\x", 100, "f.pdf"),
            new KeepCandidate(@"C:\Documents\x", 100, "f.pdf"),
        };
        var priorities = new[] { @"C:\Documents" };

        PrioritySelection.ChooseKeepIndex(members, priorities, useScore: false).Should().Be(1);
    }

    [Theory]
    [InlineData("rapport_v2.pdf", 2)]
    [InlineData("rapport v3.pdf", 3)]
    [InlineData("v4 rapport.pdf", 4)]
    [InlineData("rapport V5.pdf", 5)]
    [InlineData("docV6.pdf", 6)]
    [InlineData("rapport.pdf", 0)]
    [InlineData("covid19.pdf", 0)]
    [InlineData("notes v2 puis v10.pdf", 10)]
    public void ExtractVersion_ReadsVersionSuffix(string fileName, int expected)
    {
        PrioritySelection.ExtractVersion(fileName).Should().Be(expected);
    }

    [Fact]
    public void ChooseKeepIndex_HighestVersionWins_OverPriorityAndScore()
    {
        // La version prime : on garde v3 même s'il est hors répertoire prioritaire
        // et moins bien rangé que le v2.
        var members = new[]
        {
            new KeepCandidate(@"C:\Documents\Projets\Sous", 100, "rapport_v2.pdf"),
            new KeepCandidate(@"C:\Downloads", 100, "rapport_v3.pdf"),
        };
        var priorities = new[] { @"C:\Documents" };

        PrioritySelection.ChooseKeepIndex(members, priorities, useScore: true).Should().Be(1);
    }

    [Fact]
    public void ChooseKeepIndex_NoVersion_FallsBackToPriority()
    {
        var members = new[]
        {
            new KeepCandidate(@"C:\Downloads", 100, "rapport.pdf"),
            new KeepCandidate(@"C:\Documents", 100, "rapport.pdf"),
        };
        var priorities = new[] { @"C:\Documents" };

        PrioritySelection.ChooseKeepIndex(members, priorities, useScore: true).Should().Be(1);
    }

    [Fact]
    public void PreferableScore_PenalisesCopyAndVersionMarkers()
    {
        var clean = new KeepCandidate(@"C:\Docs", 100, "rapport.pdf");
        var copy = new KeepCandidate(@"C:\Docs", 100, "rapport (1).pdf");
        var version = new KeepCandidate(@"C:\Docs", 100, "rapport_v2.pdf");

        PrioritySelection.PreferableScore(clean).Should().BeGreaterThan(PrioritySelection.PreferableScore(copy));
        PrioritySelection.PreferableScore(clean).Should().BeGreaterThan(PrioritySelection.PreferableScore(version));
    }
}
