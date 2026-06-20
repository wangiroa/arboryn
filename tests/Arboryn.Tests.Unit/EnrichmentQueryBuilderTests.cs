using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class EnrichmentQueryBuilderTests
{
    [Fact]
    public void Build_CopiesOnlyWhitelistedKeys_ForBooks()
    {
        var fused = new Dictionary<string, string>
        {
            [MetadataKeys.Isbn] = "9782070612888",
            [MetadataKeys.Title] = "La Communauté de l'Anneau",
            [MetadataKeys.Author] = "Tolkien",
            [MetadataKeys.Year] = "1954",
            // Bruit non autorisé (doit être exclu) :
            [MetadataKeys.Codec] = "h264",
            [MetadataKeys.Resolution] = "1080p",
        };

        var query = EnrichmentQueryBuilder.Build(MediaCategory.Book, fused);

        query.Fields.Keys.Should().BeEquivalentTo(new[]
        {
            MetadataKeys.Isbn, MetadataKeys.Title, MetadataKeys.Author, MetadataKeys.Year,
        });
    }

    [Fact]
    public void Build_NeverLeaksFilenameOrPath_EvenIfPresentInMetadata()
    {
        // Garantie privacy-first : aucune clé de type chemin/nom de fichier n'est en liste blanche.
        var fused = new Dictionary<string, string>
        {
            [MetadataKeys.Title] = "Dune",
            ["filename"] = @"C:\secret\Dune (1965) [scan].pdf",
            ["path"] = @"\\nas\private\Dune.pdf",
            [MetadataKeys.Source] = @"D:\torrents\dune.epub",
        };

        var query = EnrichmentQueryBuilder.Build(MediaCategory.Book, fused);

        query.Fields.Should().ContainKey(MetadataKeys.Title);
        query.CanonicalForm().Should().NotContainAny("secret", ".pdf", ".epub", "nas", "torrents", @"\");
    }

    [Fact]
    public void Build_DerivesAuthorFromAlbumArtist_AndTitleFromAlbum_ForAudiobook()
    {
        var fused = new Dictionary<string, string>
        {
            [MetadataKeys.Album] = "Fondation",
            [MetadataKeys.AlbumArtist] = "Isaac Asimov",
        };

        var query = EnrichmentQueryBuilder.Build(MediaCategory.Audiobook, fused);

        query.Get(MetadataKeys.Title).Should().Be("Fondation");
        query.Get(MetadataKeys.Author).Should().Be("Isaac Asimov");
    }

    [Theory]
    [InlineData(MediaCategory.Photo)]
    [InlineData(MediaCategory.OfficialDocument)]
    [InlineData(MediaCategory.OtherDocument)]
    [InlineData(MediaCategory.Unknown)]
    public void Build_ReturnsEmpty_ForNonEnrichableCategories(MediaCategory category)
    {
        var fused = new Dictionary<string, string> { [MetadataKeys.Title] = "X" };
        EnrichmentQueryBuilder.Build(category, fused).IsEmpty.Should().BeTrue();
    }
}
