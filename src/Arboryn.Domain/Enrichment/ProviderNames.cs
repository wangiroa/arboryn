namespace Arboryn.Domain.Enrichment;

/// <summary>
/// Identifiants stables des providers d'enrichissement. Servent de clé dans <c>api_cache</c>,
/// de suffixe de source de métadonnée (<c>online_&lt;provider&gt;</c>) et de clé de réglage
/// (clé d'API, activation par provider).
/// </summary>
public static class ProviderNames
{
    public const string OpenLibrary = "openlibrary";
    public const string GoogleBooks = "googlebooks";
    public const string Tmdb = "tmdb";
    public const string MusicBrainz = "musicbrainz";
}
