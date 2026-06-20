using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enrichment;
using Arboryn.Domain.Enums;
using Arboryn.Infrastructure.Enrichment;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class EnrichmentProvidersTests
{
    [Fact]
    public async Task OpenLibrary_ParsesIsbnResponse_AsIdentifierMatch()
    {
        const string json = """
            { "ISBN:9782070612888": {
                "title": "La Communauté de l'Anneau",
                "authors": [{ "name": "J.R.R. Tolkien" }],
                "publishers": [{ "name": "Gallimard" }],
                "publish_date": "1972"
            } }
            """;
        var handler = new RecordingHttpMessageHandler(json);
        var provider = new OpenLibraryProvider(new HttpClient(handler), NullLogger<OpenLibraryProvider>.Instance);

        var query = new EnrichmentQuery(MediaCategory.Book, new Dictionary<string, string>
        {
            [MetadataKeys.Isbn] = "9782070612888",
        });
        var result = await provider.QueryAsync(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Match.Should().Be(EnrichmentMatchKind.Identifier);
        result.Confidence.Should().BeGreaterThan(0.9);
        result.Fields[MetadataKeys.Title].Should().Be("La Communauté de l'Anneau");
        result.Fields[MetadataKeys.Author].Should().Be("J.R.R. Tolkien");
        result.Fields[MetadataKeys.Year].Should().Be("1972");
    }

    [Fact]
    public async Task GoogleBooks_ParsesVolumeResponse_AsFuzzyMatch()
    {
        const string json = """
            { "items": [ { "volumeInfo": {
                "title": "Fondation",
                "authors": ["Isaac Asimov"],
                "publisher": "Denoël",
                "publishedDate": "1966-03",
                "industryIdentifiers": [{ "type": "ISBN_13", "identifier": "9782207301234" }]
            } } ] }
            """;
        var handler = new RecordingHttpMessageHandler(json);
        var provider = new GoogleBooksProvider(
            new HttpClient(handler), new FakeKeyring(), NullLogger<GoogleBooksProvider>.Instance);

        var query = new EnrichmentQuery(MediaCategory.Book, new Dictionary<string, string>
        {
            [MetadataKeys.Title] = "Fondation", [MetadataKeys.Author] = "Asimov",
        });
        var result = await provider.QueryAsync(query, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Match.Should().Be(EnrichmentMatchKind.Fuzzy);
        result.Fields[MetadataKeys.Title].Should().Be("Fondation");
        result.Fields[MetadataKeys.Year].Should().Be("1966");
        result.Fields[MetadataKeys.Isbn].Should().Be("9782207301234");
    }

    [Fact]
    public async Task Provider_OutgoingRequest_ContainsNoFilenameOrPath()
    {
        // Construit la requête comme en production : depuis des métadonnées contenant AUSSI un
        // chemin/nom de fichier. La liste blanche doit les exclure → ils ne sortent jamais.
        var fused = new Dictionary<string, string>
        {
            [MetadataKeys.Title] = "Dune",
            [MetadataKeys.Author] = "Herbert",
            [MetadataKeys.Source] = @"C:\secret\Dune (1965) [scan].epub",
        };
        var query = EnrichmentQueryBuilder.Build(MediaCategory.Book, fused);

        var handler = new RecordingHttpMessageHandler("{}");
        var provider = new OpenLibraryProvider(new HttpClient(handler), NullLogger<OpenLibraryProvider>.Instance);

        await provider.QueryAsync(query, CancellationToken.None);

        handler.Requests.Should().NotBeEmpty();
        var outgoing = string.Join(" ", handler.Requests.Select(u => Uri.UnescapeDataString(u.AbsoluteUri)));
        outgoing.Should().ContainAny("Dune", "dune");
        outgoing.Should().NotContainAny("secret", ".epub", @"\", "scan", "1965");
    }

    [Fact]
    public async Task Tmdb_IsNotConfigured_WithoutApiKey()
    {
        var provider = new TmdbProvider(
            new HttpClient(new RecordingHttpMessageHandler("{}")), new FakeKeyring(), NullLogger<TmdbProvider>.Instance);

        provider.IsConfigured.Should().BeFalse();

        var withKey = new TmdbProvider(
            new HttpClient(new RecordingHttpMessageHandler("{}")),
            new FakeKeyring { [ProviderNames.Tmdb] = "k" }, NullLogger<TmdbProvider>.Instance);
        withKey.IsConfigured.Should().BeTrue();
    }

    private sealed class FakeKeyring : Dictionary<string, string>, IEnrichmentKeyring
    {
        public string? ApiKey(string provider) => TryGetValue(provider, out var k) ? k : null;

        public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
