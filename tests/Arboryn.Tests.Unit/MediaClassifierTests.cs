using Arboryn.Domain.Enums;
using Arboryn.Domain.Metadata;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class MediaClassifierTests
{
    [Theory]
    [InlineData(".jpg", MediaCategory.Photo)]
    [InlineData(".jpeg", MediaCategory.Photo)]
    [InlineData(".HEIC", MediaCategory.Photo)]
    [InlineData(".nef", MediaCategory.Photo)]
    [InlineData(".mp4", MediaCategory.Video)]
    [InlineData(".MKV", MediaCategory.Video)]
    [InlineData(".webm", MediaCategory.Video)]
    [InlineData(".mp3", MediaCategory.Audiobook)]
    [InlineData(".m4b", MediaCategory.Audiobook)]
    [InlineData(".flac", MediaCategory.Audiobook)]
    [InlineData(".epub", MediaCategory.Book)]
    [InlineData(".azw3", MediaCategory.Book)]
    [InlineData(".pdf", MediaCategory.OtherDocument)]
    [InlineData(".docx", MediaCategory.OtherDocument)]
    [InlineData(".csv", MediaCategory.OtherDocument)]
    [InlineData("jpg", MediaCategory.Photo)]
    public void FromExtension_RecognisesCommonFormats(string extension, MediaCategory expected)
    {
        MediaClassifier.FromExtension(extension).Should().Be(expected);
    }

    [Theory]
    [InlineData(".xyz")]
    [InlineData("")]
    [InlineData(null)]
    public void FromExtension_UnknownOrEmpty_IsUnknown(string? extension)
    {
        MediaClassifier.FromExtension(extension!).Should().Be(MediaCategory.Unknown);
    }
}
