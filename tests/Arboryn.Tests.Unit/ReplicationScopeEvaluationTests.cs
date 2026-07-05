using Arboryn.Domain.Enums;
using Arboryn.Domain.Replication;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class ReplicationScopeEvaluationTests
{
    [Fact]
    public void All_MatchesEverything()
    {
        ScopeExpression.All.Matches(new ScopeSubject(MediaCategory.Video)).Should().BeTrue();
        ScopeExpression.All.Matches(new ScopeSubject(MediaCategory.Unknown)).Should().BeTrue();
    }

    [Fact]
    public void None_MatchesNothing()
    {
        ScopeExpression.None.Matches(new ScopeSubject(MediaCategory.Book)).Should().BeFalse();
    }

    [Fact]
    public void Category_MatchesOnlyListedCategories()
    {
        var scope = ScopeExpression.Categories(MediaCategory.Audiobook, MediaCategory.Book);

        scope.Matches(new ScopeSubject(MediaCategory.Audiobook)).Should().BeTrue();
        scope.Matches(new ScopeSubject(MediaCategory.Book)).Should().BeTrue();
        scope.Matches(new ScopeSubject(MediaCategory.Video)).Should().BeFalse();
    }

    [Theory]
    [InlineData("Investissements", true)]
    [InlineData("investissements", true)]                             // insensible à la casse
    [InlineData("Investissements/Appartement Champigny", true)]       // préfixe hiérarchique
    [InlineData("Investissements/Appartement Champigny/Factures", true)]
    [InlineData("Fiscal", false)]
    [InlineData("Investissementsbis", false)]                          // pas un vrai préfixe
    public void Subcategory_MatchesExactOrHierarchicalPrefix(string subcategory, bool expected)
    {
        var scope = ScopeExpression.Subcategories("Investissements");

        scope.Matches(new ScopeSubject(MediaCategory.OfficialDocument, subcategory)).Should().Be(expected);
    }

    [Fact]
    public void Subcategory_WithoutSubcategory_DoesNotMatch()
    {
        ScopeExpression.Subcategories("Investissements")
            .Matches(new ScopeSubject(MediaCategory.OfficialDocument, Subcategory: null))
            .Should().BeFalse();
    }

    [Fact]
    public void YearRange_RespectsClosedBounds()
    {
        var since2020 = ScopeExpression.Years(2020, null);
        since2020.Matches(new ScopeSubject(MediaCategory.Photo, Year: 2019)).Should().BeFalse();
        since2020.Matches(new ScopeSubject(MediaCategory.Photo, Year: 2020)).Should().BeTrue();
        since2020.Matches(new ScopeSubject(MediaCategory.Photo, Year: 2026)).Should().BeTrue();

        var window = ScopeExpression.Years(2000, 2010);
        window.Matches(new ScopeSubject(MediaCategory.Photo, Year: 2005)).Should().BeTrue();
        window.Matches(new ScopeSubject(MediaCategory.Photo, Year: 2011)).Should().BeFalse();
    }

    [Fact]
    public void YearRange_WithBound_RejectsSubjectWithoutYear()
    {
        ScopeExpression.Years(2020, null)
            .Matches(new ScopeSubject(MediaCategory.Photo, Year: null))
            .Should().BeFalse();
    }

    [Fact]
    public void And_RequiresAllOperands()
    {
        // category = 'Documents officiels' AND subcategory = 'Investissements'
        var scope = ScopeExpression.And(
            ScopeExpression.Categories(MediaCategory.OfficialDocument),
            ScopeExpression.Subcategories("Investissements"));

        scope.Matches(new ScopeSubject(MediaCategory.OfficialDocument, "Investissements/Factures")).Should().BeTrue();
        scope.Matches(new ScopeSubject(MediaCategory.OfficialDocument, "Fiscal")).Should().BeFalse();
        scope.Matches(new ScopeSubject(MediaCategory.Photo, "Investissements")).Should().BeFalse();
    }

    [Fact]
    public void Or_RequiresAnyOperand()
    {
        var scope = ScopeExpression.Or(
            ScopeExpression.Categories(MediaCategory.Audiobook),
            ScopeExpression.Categories(MediaCategory.Book));

        scope.Matches(new ScopeSubject(MediaCategory.Book)).Should().BeTrue();
        scope.Matches(new ScopeSubject(MediaCategory.Video)).Should().BeFalse();
    }

    [Fact]
    public void Not_InvertsInner()
    {
        var scope = ScopeExpression.Not(ScopeExpression.Categories(MediaCategory.Video));

        scope.Matches(new ScopeSubject(MediaCategory.Video)).Should().BeFalse();
        scope.Matches(new ScopeSubject(MediaCategory.Book)).Should().BeTrue();
    }

    [Fact]
    public void Composite_PhotosSince2020()
    {
        // category = 'Photos' AND year >= 2020
        var scope = ScopeExpression.And(
            ScopeExpression.Categories(MediaCategory.Photo),
            ScopeExpression.Years(2020, null));

        scope.Matches(new ScopeSubject(MediaCategory.Photo, Year: 2021)).Should().BeTrue();
        scope.Matches(new ScopeSubject(MediaCategory.Photo, Year: 2015)).Should().BeFalse();
        scope.Matches(new ScopeSubject(MediaCategory.Video, Year: 2021)).Should().BeFalse();
    }
}
