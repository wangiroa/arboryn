using Arboryn.Domain.Triage;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class TriagePatternLearnerTests
{
    [Fact]
    public void Derive_BuildsLiteralPattern_FromSourceCorrection()
    {
        var correction = new TriageCorrection(
            TriagePatternKind.Source, "…Cabinet immobilier Foncia…", "Cabinet immobilier", "Foncia");

        var pattern = TriagePatternLearner.Derive(correction);

        pattern.Should().NotBeNull();
        pattern!.Kind.Should().Be(TriagePatternKind.Source);
        pattern.Template.Should().Be("Foncia");
        pattern.LearnedFromUser.Should().BeTrue();
        pattern.Priority.Should().Be(TriagePatternLearner.LearnedPriority);
        pattern.Regex.Should().Be(@"\bFoncia\b");
    }

    [Fact]
    public void Derive_ReturnsNull_ForDateCorrections()
    {
        var correction = new TriageCorrection(TriagePatternKind.Date, "le 12/03/2024", "202401", "202403");
        TriagePatternLearner.Derive(correction).Should().BeNull();
    }

    [Fact]
    public void Derive_ReturnsNull_WhenCorrectionMatchesExtractedValue()
    {
        var correction = new TriageCorrection(TriagePatternKind.Object, "snippet", "Facture", "facture");
        TriagePatternLearner.Derive(correction).Should().BeNull();
    }

    [Fact]
    public void Derive_ReturnsNull_ForTooShortValue()
    {
        var correction = new TriageCorrection(TriagePatternKind.Source, "snippet", null, "X");
        TriagePatternLearner.Derive(correction).Should().BeNull();
    }

    [Fact]
    public void Derive_EscapesRegexMetacharacters()
    {
        var correction = new TriageCorrection(TriagePatternKind.Object, "snippet", null, "Avis (copropriété)");
        var pattern = TriagePatternLearner.Derive(correction);

        pattern.Should().NotBeNull();
        // La parenthèse est échappée et la frontière finale absente (caractère non-mot).
        pattern!.Regex.Should().Contain(@"\(copropriét");
        pattern.Regex.Should().StartWith(@"\bAvis");
    }
}
