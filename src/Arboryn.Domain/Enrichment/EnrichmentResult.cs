namespace Arboryn.Domain.Enrichment;

/// <summary>Comment un provider a apparié la requête — détermine la confiance accordée au résultat.</summary>
public enum EnrichmentMatchKind
{
    /// <summary>Appariement sur un identifiant exact (ISBN, identifiant TMDB/MusicBrainz).</summary>
    Identifier,

    /// <summary>Appariement approximatif (titre + auteur), moins sûr.</summary>
    Fuzzy,
}

/// <summary>
/// Résultat d'enrichissement renvoyé par un provider : les champs structurés trouvés (clés
/// <c>MetadataKeys</c>), la confiance de base, et le mode d'appariement. La confiance effective
/// (appliquée par le handler) peut être relevée pour un appariement par identifiant exact.
/// </summary>
public sealed record EnrichmentResult(
    string Provider,
    IReadOnlyDictionary<string, string> Fields,
    double Confidence,
    EnrichmentMatchKind Match)
{
    public bool HasFields => Fields.Count > 0;
}
