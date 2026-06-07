using Arboryn.Domain.Triage;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class FrenchDateParserTests
{
    [Theory]
    [InlineData("05/06/2024", "202406")]   // JJ/MM/AAAA (français : jour d'abord)
    [InlineData("5.6.2024", "202406")]
    [InlineData("06/2024", "202406")]       // MM/AAAA
    [InlineData("2024-06-15", "202406")]    // ISO
    [InlineData("12 mars 2024", "202403")]  // littéral avec jour
    [InlineData("mars 2024", "202403")]     // littéral mois + année
    [InlineData("3 avril 2023", "202304")]
    [InlineData("15 août 2022", "202208")]  // accent
    [InlineData("0324", "202403")]          // MMAA compact
    public void TryParse_NormalizesToYearMonth(string input, string expected)
    {
        FrenchDateParser.TryParse(input, out var yyyyMM).Should().BeTrue();
        yyyyMM.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pas une date")]
    [InlineData("2024")]        // année seule : mois inconnu
    [InlineData("99/99/2024")]  // mois/jour invalides
    public void TryParse_RejectsNonDates(string input)
    {
        FrenchDateParser.TryParse(input, out _).Should().BeFalse();
    }

    [Fact]
    public void ScanFirst_FindsDateWithinFreeText()
    {
        const string text = "Société EDF\nVotre facture\nParis, le 3 avril 2023\nMontant : 42,00 €";
        FrenchDateParser.ScanFirst(text, out var yyyyMM).Should().BeTrue();
        yyyyMM.Should().Be("202304");
    }

    [Fact]
    public void ScanFirst_ReturnsFalse_WhenNoDate()
    {
        FrenchDateParser.ScanFirst("aucune date ici", out _).Should().BeFalse();
    }
}
