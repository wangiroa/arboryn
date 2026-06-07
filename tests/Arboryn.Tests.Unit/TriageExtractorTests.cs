using Arboryn.Domain.Triage;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class TriageExtractorTests
{
    private static readonly IReadOnlyList<TriagePattern> Defaults = DefaultTriagePatterns.All;

    [Fact]
    public void Extract_PrefillsObjectAndDate_FromDefaultPatterns()
    {
        const string text = "FONCIA\nAppel de fonds n° 12\nFait le 12 mars 2024\nCopropriété Les Tilleuls";

        var result = TriageExtractor.Extract(text, Defaults);

        result.Object.Value.Should().Be("Appel de fonds");
        result.Date.Value.Should().Be("202403");
    }

    [Fact]
    public void Extract_FallsBackToCapitalizedEntity_ForSource_WhenNoSourcePattern()
    {
        const string text = "FONCIA\nAppel de fonds\nFait le 12 mars 2024";

        var result = TriageExtractor.Extract(text, Defaults);

        // Aucun motif « émis par » : la détection d'entité capitalisée prend le relais.
        result.Source.Value.Should().Be("FONCIA");
        result.Source.MatchedBy.Should().Be("CapitalizedEntityDetector");
    }

    [Fact]
    public void Extract_UsesSourcePattern_WhenHeaderMentionsEmitter()
    {
        const string text = "Reçu\nÉmis par : Caisse d'Épargne\nObjet : relevé de compte";

        var result = TriageExtractor.Extract(text, Defaults);

        result.Source.Value.Should().Be("Caisse d'Épargne");
        result.Object.Value.Should().Be("Relevé de compte");
    }

    [Fact]
    public void Extract_LearnedPattern_TakesPriority_AndIsHighConfidence()
    {
        var patterns = new List<TriagePattern>(Defaults)
        {
            new(Id: "x", TriagePatternKind.Source, @"\bFoncia\b", "Foncia",
                "Appris : Foncia", LearnedFromUser: true, Priority: TriagePatternLearner.LearnedPriority),
        };

        var result = TriageExtractor.Extract("Document émis par Foncia SA", patterns);

        result.Source.Value.Should().Be("Foncia");
        result.Source.Confidence.Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void Extract_EmptyText_YieldsEmptyFields()
    {
        var result = TriageExtractor.Extract(string.Empty, Defaults);

        result.Source.HasValue.Should().BeFalse();
        result.Object.HasValue.Should().BeFalse();
        result.Date.HasValue.Should().BeFalse();
    }

    [Fact]
    public void Extract_IgnoresInvalidLearnedRegex_WithoutThrowing()
    {
        var patterns = new List<TriagePattern>
        {
            new(Id: "bad", TriagePatternKind.Object, "([unclosed", "X",
                null, LearnedFromUser: true, Priority: 500),
        };

        var act = () => TriageExtractor.Extract("une facture", patterns);
        act.Should().NotThrow();
    }
}
