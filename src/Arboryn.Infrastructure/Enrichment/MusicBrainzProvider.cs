using System.Text.Json;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enrichment;
using Arboryn.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Arboryn.Infrastructure.Enrichment;

/// <summary>
/// Provider MusicBrainz (livres audio / audio), sans clé. Recherche une <c>release</c> par titre
/// (+ artiste). Requiert un en-tête User-Agent identifiant l'application (configuré au niveau du
/// HttpClient). Appariement approximatif.
/// </summary>
public sealed class MusicBrainzProvider : HttpMetadataProvider
{
    public MusicBrainzProvider(HttpClient http, ILogger<MusicBrainzProvider> logger)
        : base(http, logger)
    {
    }

    public override string Name => ProviderNames.MusicBrainz;

    public override bool CanEnrich(MediaCategory category) => category == MediaCategory.Audiobook;

    public override async Task<EnrichmentResult?> QueryAsync(EnrichmentQuery query, CancellationToken cancellationToken)
    {
        var title = query.Get(MetadataKeys.Title);
        if (title is null)
        {
            return null;
        }

        var lucene = $"release:\"{title}\"";
        var author = query.Get(MetadataKeys.Author);
        if (author is not null)
        {
            lucene += $" AND artist:\"{author}\"";
        }

        var url = $"https://musicbrainz.org/ws/2/release/?query={Escape(lucene)}&fmt=json&limit=1";
        using var doc = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (doc is null || !doc.RootElement.TryGetProperty("releases", out var releases)
            || releases.ValueKind != JsonValueKind.Array || releases.GetArrayLength() == 0)
        {
            return null;
        }

        var first = releases[0];
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var foundTitle = GetString(first, "title");
        if (!string.IsNullOrWhiteSpace(foundTitle))
        {
            fields[MetadataKeys.Title] = foundTitle!.Trim();
        }

        var artist = FirstArtist(first);
        if (artist is not null)
        {
            fields[MetadataKeys.Author] = artist;
        }

        var year = Year(GetString(first, "date"));
        if (year is not null)
        {
            fields[MetadataKeys.Year] = year;
        }

        return fields.Count > 0 ? new EnrichmentResult(Name, fields, 0.7, EnrichmentMatchKind.Fuzzy) : null;
    }

    /// <summary>Premier nom d'artiste dans <c>artist-credit[].name</c>.</summary>
    private static string? FirstArtist(JsonElement release)
        => release.TryGetProperty("artist-credit", out var credits)
            && credits.ValueKind == JsonValueKind.Array && credits.GetArrayLength() > 0
            ? GetString(credits[0], "name")
            : null;
}
