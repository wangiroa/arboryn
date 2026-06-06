using Arboryn.Domain.Enums;
using Arboryn.Domain.Metadata;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class MediaFilterClassifierTests
{
    [Theory]
    [InlineData(".mp3", MediaFilterType.Audio)]
    [InlineData(".m4b", MediaFilterType.Audio)]
    [InlineData(".mkv", MediaFilterType.Video)]
    [InlineData(".heic", MediaFilterType.Photo)]
    [InlineData(".cbz", MediaFilterType.Comic)]
    [InlineData(".docx", MediaFilterType.Document)]
    [InlineData(".epub", MediaFilterType.Ebook)]
    public void FromExtension_MapsUnambiguousExtensions(string extension, MediaFilterType expected)
    {
        MediaFilterClassifier.FromExtension(extension).Should().Contain(expected);
    }

    [Fact]
    public void FromExtension_Pdf_BelongsToDocumentEbookAndComic()
    {
        MediaFilterClassifier.FromExtension(".pdf")
            .Should().BeEquivalentTo(new[]
            {
                MediaFilterType.Document, MediaFilterType.Ebook, MediaFilterType.Comic,
            });
    }

    [Fact]
    public void FromExtension_Jpg_BelongsToPhotoAndComic()
    {
        MediaFilterClassifier.FromExtension(".jpg")
            .Should().BeEquivalentTo(new[] { MediaFilterType.Photo, MediaFilterType.Comic });
    }

    [Theory]
    [InlineData("jpg")]   // sans point
    [InlineData(".JPG")]  // casse
    public void FromExtension_IsCaseInsensitiveAndDotOptional(string extension)
    {
        MediaFilterClassifier.FromExtension(extension).Should().Contain(MediaFilterType.Photo);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(".xyz")]
    public void FromExtension_UnknownOrEmpty_ReturnsEmptySet(string? extension)
    {
        MediaFilterClassifier.FromExtension(extension).Should().BeEmpty();
    }
}
