using Arboryn.Domain.Metadata;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class FilenameCleanerTests
{
    [Fact]
    public void Extract_MoviePattern_ParsesAllFields()
    {
        var meta = FilenameCleaner.Extract("Inception.2010.1080p.BluRay.x264-RARBG.mkv");

        meta.CleanTitle.Should().Be("Inception");
        meta.Year.Should().Be(2010);
        meta.Resolution.Should().Be("1080p");
        meta.Source.Should().Be("BluRay");
        meta.Codec.Should().Be("x264");
        meta.ReleaseGroup.Should().Be("RARBG");
    }

    [Fact]
    public void Extract_UhdMoviePattern_HandlesMultipleTechnicalTags()
    {
        var meta = FilenameCleaner.Extract("The.Matrix.1999.2160p.UHD.BluRay.x265.HDR.DTS-FGT.mkv");

        meta.CleanTitle.Should().Be("The Matrix");
        meta.Year.Should().Be(1999);
        meta.Resolution.Should().Be("2160p");
        meta.Codec.Should().Be("x265");
        meta.Audio.Should().Be("DTS");
        meta.ReleaseGroup.Should().Be("FGT");
    }

    [Fact]
    public void Extract_TvSeriesPattern_KeepsSeasonEpisodeInTitle()
    {
        var meta = FilenameCleaner.Extract("Show.Name.S01E01.720p.WEBRip-GROUP.mkv");

        // S01E01 reste dans le titre (ce n'est pas un tag technique connu).
        meta.CleanTitle.Should().Be("Show Name S01E01");
        meta.Resolution.Should().Be("720p");
        meta.Source.Should().Be("WEBRip");
        meta.ReleaseGroup.Should().Be("GROUP");
    }

    [Fact]
    public void Extract_FrenchVostfrTag_DetectsLanguage()
    {
        var meta = FilenameCleaner.Extract("Le Fabuleux Destin (2001) 1080p VOSTFR BluRay x264.mkv");

        meta.CleanTitle.Should().Be("Le Fabuleux Destin");
        meta.Year.Should().Be(2001);
        meta.Language.Should().Be("VOSTFR");
        meta.Resolution.Should().Be("1080p");
        meta.Source.Should().Be("BluRay");
    }

    [Fact]
    public void Extract_PhotoFilename_NoTagsExtracted()
    {
        var meta = FilenameCleaner.Extract("DSC_1234.jpg");

        meta.CleanTitle.Should().Be("DSC 1234");
        meta.Year.Should().BeNull();
        meta.Resolution.Should().BeNull();
    }

    [Fact]
    public void Extract_PlainDocument_OnlyTitle()
    {
        var meta = FilenameCleaner.Extract("Rapport final.pdf");

        meta.CleanTitle.Should().Be("Rapport final");
        meta.Year.Should().BeNull();
    }

    [Fact]
    public void Extract_YearInTitle_IsExtractedNotKept()
    {
        var meta = FilenameCleaner.Extract("Annual Report 2023.pdf");

        meta.CleanTitle.Should().Be("Annual Report");
        meta.Year.Should().Be(2023);
    }

    [Fact]
    public void Extract_EmptyInput_ReturnsEmpty()
    {
        var meta = FilenameCleaner.Extract("");

        meta.CleanTitle.Should().BeEmpty();
        meta.Year.Should().BeNull();
    }
}
