using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class CategoryRefinerTests
{
    [Fact]
    public void Document_WithIsbn_IsPromotedToBook()
    {
        var metadata = new Dictionary<string, string>
        {
            [MetadataKeys.Title] = "Un roman",
            [MetadataKeys.Isbn] = "9782070612888",
        };

        CategoryRefiner.Refine(MediaCategory.OtherDocument, metadata)
            .Should().Be(MediaCategory.Book);
    }

    [Fact]
    public void Document_WithoutIsbn_StaysDocument()
    {
        var metadata = new Dictionary<string, string> { [MetadataKeys.Title] = "Une facture" };

        CategoryRefiner.Refine(MediaCategory.OtherDocument, metadata)
            .Should().Be(MediaCategory.OtherDocument);
    }

    [Fact]
    public void Document_WithBlankIsbn_StaysDocument()
    {
        var metadata = new Dictionary<string, string> { [MetadataKeys.Isbn] = "   " };

        CategoryRefiner.Refine(MediaCategory.OtherDocument, metadata)
            .Should().Be(MediaCategory.OtherDocument);
    }

    [Theory]
    [InlineData(MediaCategory.Photo)]
    [InlineData(MediaCategory.Audiobook)]
    [InlineData(MediaCategory.Video)]
    [InlineData(MediaCategory.Book)]
    public void NonDocumentCategories_AreNeverChanged_EvenWithIsbn(MediaCategory preliminary)
    {
        var metadata = new Dictionary<string, string> { [MetadataKeys.Isbn] = "9782070612888" };

        CategoryRefiner.Refine(preliminary, metadata).Should().Be(preliminary);
    }
}
