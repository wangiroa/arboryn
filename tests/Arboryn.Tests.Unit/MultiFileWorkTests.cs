using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class MultiFileWorkTests
{
    [Theory]
    [InlineData(MediaCategory.Audiobook, "001 chapitre 1", true)]
    [InlineData(MediaCategory.Comic, "012", true)]
    [InlineData(MediaCategory.Audiobook, "Le Hobbit", false)]
    [InlineData(MediaCategory.Book, "001 chapitre 1", false)] // catégorie non multi-fichiers
    public void IsPartFile_DecidesOnFileStem_NotTitleTag(
        MediaCategory category, string fileStem, bool expected)
    {
        MultiFileWork.IsPartFile(category, fileStem).Should().Be(expected);
    }

    [Theory]
    [InlineData("Histoire totale - Olivier Wieviorka", "Olivier Wieviorka", "Histoire totale")]
    [InlineData("Olivier Wieviorka - Histoire totale", "Olivier Wieviorka", "Histoire totale")]
    [InlineData("Histoire totale", "Olivier Wieviorka", "Histoire totale")] // auteur absent → verbatim
    public void WorkTitle_StripsKnownAuthor(string directory, string author, string expected)
    {
        MultiFileWork.WorkTitle(directory, author).Should().Be(expected);
    }

    [Fact]
    public void NumberParts_PadsToConstantWidth_FromFirstFile()
    {
        // 10 chapitres numérotés depuis le fichier : largeur 2, démarre à 01 (pas de « (2) »).
        var files = Enumerable.Range(1, 10)
            .Select(n => ($"{n:000} chapitre {n}", (string?)null))
            .ToList();

        var labels = MultiFileWork.NumberParts(files);

        labels.Should().HaveCount(10);
        labels[0].Should().Be("01");
        labels[8].Should().Be("09");
        labels[9].Should().Be("10");
    }

    [Fact]
    public void NumberParts_WidthFollowsLargestNumber_OverHundred()
    {
        var files = Enumerable.Range(1, 100)
            .Select(n => ($"{n} chapitre {n}", (string?)null))
            .ToList();

        var labels = MultiFileWork.NumberParts(files);

        labels[0].Should().Be("001");
        labels[99].Should().Be("100");
    }

    [Fact]
    public void NumberParts_FallsBackToAlphabeticalOrder_WhenNoNumbers()
    {
        // Aucun numéro exploitable → renumérotation 1..N par ordre alphabétique du nom.
        var files = new List<(string, string?)>
        {
            ("intro", null),
            ("avant-propos", null),
            ("conclusion", null),
        };

        var labels = MultiFileWork.NumberParts(files);

        labels[0].Should().Be("3"); // intro
        labels[1].Should().Be("1"); // avant-propos
        labels[2].Should().Be("2"); // conclusion
    }

    [Fact]
    public void NumberParts_UsesTrackTag_WhenFilenameHasNoNumber()
    {
        var files = new List<(string, string?)>
        {
            ("intro", "1"),
            ("milieu", "2"),
            ("fin", "3"),
        };

        var labels = MultiFileWork.NumberParts(files);

        labels.Should().Equal("1", "2", "3");
    }
}
