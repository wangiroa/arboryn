using System.Text.Json;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enrichment;
using Arboryn.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Arboryn.Infrastructure.Enrichment;

/// <summary>
/// Provider Google Books (livres / BD), repli d'OpenLibrary. Clé d'API optionnelle (sinon
/// quota anonyme). Recherche par ISBN (identifiant) ou titre + auteur (approximatif).
/// </summary>
public sealed class GoogleBooksProvider : HttpMetadataProvider
{
    private readonly IEnrichmentKeyring _keyring;

    public GoogleBooksProvider(HttpClient http, IEnrichmentKeyring keyring, ILogger<GoogleBooksProvider> logger)
        : base(http, logger)
        => _keyring = keyring;

    public override string Name => ProviderNames.GoogleBooks;

    public override bool CanEnrich(MediaCategory category)
        => category is MediaCategory.Book or MediaCategory.Comic;

    public override async Task<EnrichmentResult?> QueryAsync(EnrichmentQuery query, CancellationToken cancellationToken)
    {
        var isbn = query.Get(MetadataKeys.Isbn);
        var title = query.Get(MetadataKeys.Title);
        var author = query.Get(MetadataKeys.Author);

        string q;
        EnrichmentMatchKind match;
        double confidence;
        if (isbn is not null)
        {
            q = $"isbn:{Escape(isbn)}";
            match = EnrichmentMatchKind.Identifier;
            confidence = 0.95;
        }
        else if (title is not null)
        {
            q = $"intitle:{Escape(title)}";
            if (author is not null)
            {
                q += $"+inauthor:{Escape(author)}";
            }

            match = EnrichmentMatchKind.Fuzzy;
            confidence = 0.7;
        }
        else
        {
            return null;
        }

        var url = $"https://www.googleapis.com/books/v1/volumes?q={q}&maxResults=1";
        var key = _keyring.ApiKey(Name);
        if (key is not null)
        {
            url += $"&key={Escape(key)}";
        }

        using var doc = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null || !doc.RootElement.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0
            || !items[0].TryGetProperty("volumeInfo", out var info))
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(fields, MetadataKeys.Title, GetString(info, "title"));
        Add(fields, MetadataKeys.Author, FirstArrayString(info, "authors"));
        Add(fields, MetadataKeys.Publisher, GetString(info, "publisher"));
        Add(fields, MetadataKeys.Year, Year(GetString(info, "publishedDate")));
        Add(fields, MetadataKeys.Isbn, isbn ?? Isbn13(info));

        return fields.Count > 0 ? new EnrichmentResult(Name, fields, confidence, match) : null;
    }

    private static void Add(IDictionary<string, string> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields[key] = value!.Trim();
        }
    }

    private static string? FirstArrayString(JsonElement element, string property)
        => element.TryGetProperty(property, out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0
            ? arr[0].GetString()
            : null;

    /// <summary>Cherche un identifiant ISBN_13 dans <c>industryIdentifiers</c>.</summary>
    private static string? Isbn13(JsonElement info)
    {
        if (!info.TryGetProperty("industryIdentifiers", out var ids) || ids.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var id in ids.EnumerateArray())
        {
            if (string.Equals(GetString(id, "type"), "ISBN_13", StringComparison.Ordinal))
            {
                return GetString(id, "identifier");
            }
        }

        return null;
    }
}
