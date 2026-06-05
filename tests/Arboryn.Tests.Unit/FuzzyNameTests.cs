using Arboryn.Domain.Matching;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class FuzzyNameTests
{
    [Fact]
    public void Similarity_IdenticalNames_IsOne()
    {
        FuzzyName.Similarity("hamlet", "hamlet").Should().Be(1.0);
    }

    [Fact]
    public void Similarity_HamletVariant_IsAboveDefaultThreshold()
    {
        // Critère Inc 2 : « Hamlet.pdf » et « Hamlet_v2.pdf » détectés similaires.
        // Noms canoniques : "hamlet" vs "hamlet v2".
        FuzzyName.Similarity("hamlet", "hamlet v2").Should().BeGreaterThanOrEqualTo(0.85);
    }

    [Fact]
    public void Similarity_OneCharacterTypo_IsHigh()
    {
        // Coquilles sur les lettres uniquement (les écarts numériques sont gérés ailleurs).
        FuzzyName.Similarity("rapport final", "rapport finaal").Should().BeGreaterThanOrEqualTo(0.85);
    }

    [Fact]
    public void Similarity_UnrelatedNames_IsLow()
    {
        FuzzyName.Similarity("hamlet", "macbeth").Should().BeLessThan(0.5);
    }

    [Fact]
    public void Similarity_EmptyInput_IsZero()
    {
        FuzzyName.Similarity("", "hamlet").Should().Be(0.0);
    }

    [Theory]
    // Chapitres / parts / tomes / volumes / épisodes : parties distinctes d'une même œuvre.
    [InlineData("hamlet chapitre 1", "hamlet chapitre 2")]
    [InlineData("hamlet chapter 1", "hamlet chapter 2")]
    [InlineData("hamlet part 1", "hamlet part 2")]
    [InlineData("hamlet partie 1", "hamlet partie 2")]
    [InlineData("hamlet vol 1", "hamlet vol 2")]
    [InlineData("hamlet volume 1", "hamlet volume 3")]
    [InlineData("hamlet tome 1", "hamlet tome 2")]
    [InlineData("hamlet ep 1", "hamlet ep 2")]
    [InlineData("hamlet episode 1", "hamlet episode 5")]
    [InlineData("hamlet", "hamlet chapitre 2")]
    // Séquences photo / numéros annuels : pas des doublons.
    [InlineData("img 45", "img 46")]
    [InlineData("dsc 1234", "dsc 1235")]
    [InlineData("photo 100", "photo 101")]
    [InlineData("rapport 2020", "rapport 2021")]
    public void Similarity_DifferOnlyByPartitionTokens_IsZero(string left, string right)
    {
        FuzzyName.Similarity(left, right).Should().Be(0.0);
    }

    [Theory]
    // Dates / timestamps (≥ 6 chiffres) : on les laisse groupables — utile pour purger
    // des fichiers de travail intermédiaires sauvegardés à des dates voisines.
    [InlineData("doc 20230501", "doc 20230502")]
    [InlineData("doc 20230501 120000", "doc 20230501 120001")]
    [InlineData("backup 20230501 230000", "backup 20230501 230001")]
    public void Similarity_DifferByDateLikeToken_StaysGroupable(string left, string right)
    {
        FuzzyName.Similarity(left, right).Should().BeGreaterThanOrEqualTo(0.85);
    }

    [Fact]
    public void Similarity_SameChapter_DifferentVersion_StaysGroupable()
    {
        // Versions du même chapitre — toujours groupables.
        FuzzyName.Similarity("hamlet chapter 1", "hamlet chapter 1 v2")
            .Should().BeGreaterThanOrEqualTo(0.85);
    }

    [Fact]
    public void Similarity_DifferentBaseAndChapter_StaysBelowDefaultThreshold()
    {
        // Bases différentes : pas la cible du filtre marqueur — le score brut suffit
        // à les exclure naturellement du seuil par défaut (0,85).
        var score = FuzzyName.Similarity("hamlet chapitre 1", "macbeth chapitre 1");
        score.Should().BeLessThan(0.85);
    }

    [Theory]
    // Cas signalé par l'utilisateur (2026-05-31) : 4 fichiers « 16 - Chapitre 16.mp3 »
    // dans 4 répertoires d'audiobooks différents groupés à tort par la détection floue
    // via les tokens partagés « 16 » + « chapitre ». Un nom qui n'est que partition
    // n'identifie pas l'œuvre — le dossier parent le fait.
    [InlineData("16 chapitre 16", "ivanhoe chapitre 16")]
    [InlineData("16 chapitre 16", "16 chapitre 16")]
    [InlineData("chapitre 16", "chapitre 16")]
    [InlineData("chapitre 16", "ivanhoe chapitre 16")]
    [InlineData("track 03", "track 03")]
    [InlineData("01", "01")]
    [InlineData("disc 1", "disc 1")]
    [InlineData("partie 4", "asimov fondation partie 4")]
    public void Similarity_PartitionOnlyName_IsZero(string left, string right)
    {
        // Tout nom dont le résidu (après strip des marqueurs de partition et
        // chiffres autonomes) est < 3 caractères alphanumériques retourne 0,
        // même contre un nom riche, et même si les deux noms sont identiques.
        FuzzyName.Similarity(left, right).Should().Be(0.0);
    }

    [Theory]
    // Pas de régression : les noms qui *contiennent* des marqueurs de partition
    // mais qui ont un résidu identitaire substantiel restent groupables si la
    // partie identitaire correspond.
    [InlineData("hamlet chapter 1", "hamlet chapter 1 v2")]    // versions du même chapitre
    [InlineData("foundation 01 v2", "foundation 01")]          // résidu = « foundation v2 » / « foundation »
    public void Similarity_PartitionMarkers_WithSubstantialResidual_StaysGroupable(string left, string right)
    {
        FuzzyName.Similarity(left, right).Should().BeGreaterThanOrEqualTo(0.85);
    }
}
