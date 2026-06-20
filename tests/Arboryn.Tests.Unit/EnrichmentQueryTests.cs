using Arboryn.Domain.Enrichment;
using Arboryn.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class EnrichmentQueryTests
{
    [Fact]
    public void CanonicalForm_NormalizesCaseAccentsAndWhitespace()
    {
        var query = new EnrichmentQuery(MediaCategory.Book, new Dictionary<string, string>
        {
            ["title"] = "  Le  Château   d'Été ",
            ["author"] = "Émile ZOLA",
        });

        query.CanonicalForm().Should().Be("author=emile zola&title=le chateau d'ete");
    }

    [Fact]
    public void CacheKey_IsStable_AcrossEquivalentQueries()
    {
        var a = new EnrichmentQuery(MediaCategory.Book, new Dictionary<string, string>
        {
            ["title"] = "Fondation", ["author"] = "Asimov",
        });
        var b = new EnrichmentQuery(MediaCategory.Book, new Dictionary<string, string>
        {
            ["author"] = "  ASIMOV ", ["title"] = "fondation",
        });

        a.CacheKey().Should().Be(b.CacheKey());
    }

    [Fact]
    public void CacheKey_DiffersByCategory()
    {
        var fields = new Dictionary<string, string> { ["title"] = "Dune" };
        new EnrichmentQuery(MediaCategory.Book, fields).CacheKey()
            .Should().NotBe(new EnrichmentQuery(MediaCategory.Video, fields).CacheKey());
    }

    [Fact]
    public void IsEmpty_True_WhenNoUsableFields()
    {
        new EnrichmentQuery(MediaCategory.Book, new Dictionary<string, string> { ["title"] = "  " })
            .IsEmpty.Should().BeTrue();
        new EnrichmentQuery(MediaCategory.Book, new Dictionary<string, string>())
            .IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Get_ReturnsTrimmedValue_OrNull()
    {
        var query = new EnrichmentQuery(MediaCategory.Book, new Dictionary<string, string>
        {
            ["isbn"] = "9782070612888", ["title"] = "  ",
        });

        query.Get("isbn").Should().Be("9782070612888");
        query.Get("title").Should().BeNull();
        query.Get("missing").Should().BeNull();
    }
}
