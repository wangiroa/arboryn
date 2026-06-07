using Arboryn.Domain.Triage;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

/// <summary>Inc 7 — persistance du triage : patterns (seed/ajout) et corrections (apprentissage).</summary>
public class TriageRepositoryTests
{
    [Fact]
    public async Task EnsureDefaultPatterns_SeedsOnce_AndIsIdempotent()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteTriageRepository(db.Factory);

        var first = await repo.EnsureDefaultPatternsAsync(DefaultTriagePatterns.All, CancellationToken.None);
        var second = await repo.EnsureDefaultPatternsAsync(DefaultTriagePatterns.All, CancellationToken.None);

        first.Should().Be(DefaultTriagePatterns.All.Count);
        second.Should().Be(0);

        var active = await repo.GetActivePatternsAsync(CancellationToken.None);
        active.Should().HaveCount(DefaultTriagePatterns.All.Count);
        active.Should().BeInDescendingOrder(p => p.Priority);
    }

    [Fact]
    public async Task AddPattern_ThenPatternExists_IsTrue()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteTriageRepository(db.Factory);

        var pattern = new TriagePattern(
            Id: string.Empty, TriagePatternKind.Source, @"\bFoncia\b", "Foncia",
            "Appris : Foncia", LearnedFromUser: true, Priority: 200);

        var id = await repo.AddPatternAsync(pattern, CancellationToken.None);
        id.Should().NotBeNullOrEmpty();

        (await repo.PatternExistsAsync(TriagePatternKind.Source, @"\bFoncia\b", CancellationToken.None))
            .Should().BeTrue();
        (await repo.PatternExistsAsync(TriagePatternKind.Object, @"\bFoncia\b", CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Corrections_RoundTrip_ThroughUnderivedAndMarkDerived()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repo = new SqliteTriageRepository(db.Factory);

        var correction = new TriageCorrection(
            TriagePatternKind.Source, "…Foncia…", "Cabinet", "Foncia");
        await repo.AddCorrectionAsync(instanceId: null, correction, CancellationToken.None);

        var underived = await repo.GetUnderivedCorrectionsAsync(CancellationToken.None);
        underived.Should().HaveCount(1);
        underived[0].Correction.CorrectedValue.Should().Be("Foncia");

        var patternId = await repo.AddPatternAsync(
            new TriagePattern(string.Empty, TriagePatternKind.Source, @"\bFoncia\b", "Foncia", null, true, 200),
            CancellationToken.None);
        await repo.MarkCorrectionDerivedAsync(underived[0].Id, patternId, CancellationToken.None);

        (await repo.GetUnderivedCorrectionsAsync(CancellationToken.None)).Should().BeEmpty();
    }
}
