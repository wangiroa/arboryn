using System.Text.Json;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enrichment;
using Arboryn.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Arboryn.Infrastructure.Enrichment;

/// <summary>
/// Provider OpenLibrary (livres / BD), sans clé. Interroge par ISBN (appariement par identifiant,
/// forte confiance) sinon par titre + auteur (appariement approximatif). Provider primaire pour
/// les livres ; Google Books sert de repli.
/// </summary>
public sealed class OpenLibraryProvider : HttpMetadataProvider
{
    public OpenLibraryProvider(HttpClient http, ILogger<OpenLibraryProvider> logger)
        : base(http, logger)
    {
    }

    public override string Name => ProviderNames.OpenLibrary;

    public override bool CanEnrich(MediaCategory category)
        => category is MediaCategory.Book or MediaCategory.Comic;

    public override async Task<EnrichmentResult?> QueryAsync(EnrichmentQuery query, CancellationToken cancellationToken)
    {
        var isbn = query.Get(MetadataKeys.Isbn);
        if (isbn is not null)
        {
            var result = await QueryByIsbnAsync(isbn, cancellationToken).ConfigureAwait(false);
            if (result is not null)
            {
                return result;
            }
        }

        var title = query.Get(MetadataKeys.Title);
        var author = query.Get(MetadataKeys.Author);
        if (title is not null)
        {
            return await QueryBySearchAsync(title, author, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<EnrichmentResult?> QueryByIsbnAsync(string isbn, CancellationToken cancellationToken)
    {
        var url = $"https://openlibrary.org/api/books?bibkeys=ISBN:{Escape(isbn)}&format=json&jscmd=data";
        using var doc = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return null;
        }

        if (!doc.RootElement.TryGetProperty($"ISBN:{isbn}", out var book))
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(fields, MetadataKeys.Title, GetString(book, "title"));
        Add(fields, MetadataKeys.Author, FirstName(book, "authors"));
        Add(fields, MetadataKeys.Publisher, FirstName(book, "publishers"));
        Add(fields, MetadataKeys.Year, Year(GetString(book, "publish_date")));
        Add(fields, MetadataKeys.Isbn, isbn);

        return fields.Count > 0
            ? new EnrichmentResult(Name, fields, 0.95, EnrichmentMatchKind.Identifier)
            : null;
    }

    private async Task<EnrichmentResult?> QueryBySearchAsync(string title, string? author, CancellationToken cancellationToken)
    {
        var url = $"https://openlibrary.org/search.json?title={Escape(title)}&limit=1";
        if (author is not null)
        {
            url += $"&author={Escape(author)}";
        }

        using var doc = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null || !doc.RootElement.TryGetProperty("docs", out var docs)
            || docs.ValueKind != JsonValueKind.Array || docs.GetArrayLength() == 0)
        {
            return null;
        }

        var first = docs[0];
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(fields, MetadataKeys.Title, GetString(first, "title"));
        Add(fields, MetadataKeys.Author, FirstArrayString(first, "author_name"));
        if (first.TryGetProperty("first_publish_year", out var year) && year.ValueKind == JsonValueKind.Number)
        {
            fields[MetadataKeys.Year] = year.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return fields.Count > 0
            ? new EnrichmentResult(Name, fields, 0.7, EnrichmentMatchKind.Fuzzy)
            : null;
    }

    private static void Add(IDictionary<string, string> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields[key] = value!.Trim();
        }
    }

    /// <summary>Nom du premier élément d'un tableau d'objets <c>[{ "name": ... }]</c>.</summary>
    private static string? FirstName(JsonElement element, string property)
        => element.TryGetProperty(property, out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0
            ? GetString(arr[0], "name")
            : null;

    private static string? FirstArrayString(JsonElement element, string property)
        => element.TryGetProperty(property, out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0
            ? arr[0].GetString()
            : null;
}
