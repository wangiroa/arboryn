using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class TemplateFieldsTests
{
    [Fact]
    public void From_AddsExtension_WithoutDotAndLowercased()
    {
        var fields = TemplateFields.From(MediaCategory.Audiobook, new Dictionary<string, string>(), ".M4B");
        fields["ext"].Should().Be("m4b");
    }

    [Fact]
    public void From_DerivesAuthor_FromAlbumArtist_WhenAuthorMissing()
    {
        var fused = new Dictionary<string, string> { [MetadataKeys.AlbumArtist] = "Asimov" };
        var fields = TemplateFields.From(MediaCategory.Audiobook, fused, ".m4b");
        fields[MetadataKeys.Author].Should().Be("Asimov");
    }

    [Fact]
    public void From_KeepsExplicitAuthor_OverAlias()
    {
        var fused = new Dictionary<string, string>
        {
            [MetadataKeys.Author] = "Tolkien",
            [MetadataKeys.AlbumArtist] = "Narrateur",
        };
        var fields = TemplateFields.From(MediaCategory.Book, fused, ".epub");
        fields[MetadataKeys.Author].Should().Be("Tolkien");
    }

    [Fact]
    public void From_DerivesTitle_FromAlbum_WhenTitleMissing()
    {
        var fused = new Dictionary<string, string> { [MetadataKeys.Album] = "Fondation" };
        var fields = TemplateFields.From(MediaCategory.Audiobook, fused, ".m4b");
        fields[MetadataKeys.Title].Should().Be("Fondation");
    }
}
