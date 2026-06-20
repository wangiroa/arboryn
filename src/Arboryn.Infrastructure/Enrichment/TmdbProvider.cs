using System.Text.Json;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enrichment;
using Arboryn.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Arboryn.Infrastructure.Enrichment;

/// <summary>
/// Provider TMDB (films/vidéos). Nécessite une clé d'API (Settings) ; <see cref="IsConfigured"/>
/// est faux sans clé, et le provider est alors ignoré. Recherche par titre (+ année).
/// </summary>
public sealed class TmdbProvider : HttpMetadataProvider
{
    private readonly IEnrichmentKeyring _keyring;

    public TmdbProvider(HttpClient http, IEnrichmentKeyring keyring, ILogger<TmdbProvider> logger)
        : base(http, logger)
        => _keyring = keyring;

    public override string Name => ProviderNames.Tmdb;

    public override bool CanEnrich(MediaCategory category) => category == MediaCategory.Video;

    public override bool IsConfigured => _keyring.ApiKey(Name) is not null;

    public override async Task<EnrichmentResult?> QueryAsync(EnrichmentQuery query, CancellationToken cancellationToken)
    {
        var key = _keyring.ApiKey(Name);
        var title = query.Get(MetadataKeys.Title);
        if (key is null || title is null)
        {
            return null;
        }

        var url = $"https://api.themoviedb.org/3/search/movie?api_key={Escape(key)}&query={Escape(title)}";
        var year = query.Get(MetadataKeys.Year);
        if (year is not null)
        {
            url += $"&year={Escape(year)}";
        }

        using var doc = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null || !doc.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
        {
            return null;
        }

        var first = results[0];
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var foundTitle = GetString(first, "title");
        if (!string.IsNullOrWhiteSpace(foundTitle))
        {
            fields[MetadataKeys.Title] = foundTitle!.Trim();
        }

        var releaseYear = Year(GetString(first, "release_date"));
        if (releaseYear is not null)
        {
            fields[MetadataKeys.Year] = releaseYear;
        }

        return fields.Count > 0 ? new EnrichmentResult(Name, fields, 0.75, EnrichmentMatchKind.Fuzzy) : null;
    }
}
