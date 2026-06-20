using Arboryn.Domain.Matching;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class PartitionMarkersTests
{
    [Theory]
    // Noms qui ne sont que position : le dossier parent identifie l'œuvre, pas le fichier.
    [InlineData("001 chapitre 1")]
    [InlineData("chapitre 16")]
    [InlineData("16 - chapitre 16")]
    [InlineData("track 03")]
    [InlineData("01")]
    [InlineData("disc 1")]
    public void IsPositionOnly_PartitionOnlyNames_True(string name)
    {
        PartitionMarkers.IsPositionOnly(name).Should().BeTrue();
    }

    [Theory]
    // Noms portant une identité propre, même avec un marqueur de partition.
    [InlineData("Histoire totale de la seconde guerre mondiale")]
    [InlineData("Fondation")]
    [InlineData("hamlet chapitre 1")]
    public void IsPositionOnly_NamesWithIdentity_False(string name)
    {
        PartitionMarkers.IsPositionOnly(name).Should().BeFalse();
    }

    [Theory]
    [InlineData("001 chapitre 1", 1)]   // nombre en tête prioritaire
    [InlineData("12 - chapitre 12", 12)]
    [InlineData("chapitre 7", 7)]        // à défaut, le numéro du marqueur
    [InlineData("track 03", 3)]
    [InlineData("001", 1)]
    public void FirstNumber_ReturnsSequenceNumber(string name, int expected)
    {
        PartitionMarkers.FirstNumber(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("Histoire totale")]
    [InlineData("intro")]
    public void FirstNumber_NoNumber_ReturnsNull(string name)
    {
        PartitionMarkers.FirstNumber(name).Should().BeNull();
    }
}
