using Arboryn.Domain.Enrichment;
using Arboryn.Domain.Enums;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Provider d'enrichissement de métadonnées en ligne (OpenLibrary, Google Books, TMDB,
/// MusicBrainz…). Contrat <em>privacy-first</em> : l'implémentation ne doit construire sa
/// requête réseau qu'à partir des champs de <see cref="EnrichmentQuery.Fields"/> — jamais
/// d'un nom de fichier ou chemin (qui ne sont de toute façon pas transmis).
/// </summary>
public interface IMetadataProvider
{
    /// <summary>Nom stable du provider (cf. <see cref="ProviderNames"/>).</summary>
    string Name { get; }

    /// <summary>Vrai si ce provider peut enrichir cette catégorie.</summary>
    bool CanEnrich(MediaCategory category);

    /// <summary>
    /// Vrai si le provider est utilisable (ex. clé d'API présente pour TMDB). Un provider
    /// indisponible est ignoré sans erreur.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Interroge le provider et renvoie un résultat, ou <c>null</c> si aucune correspondance.
    /// Effectue un appel réseau ; l'orchestrateur ne l'appelle que si le mode en ligne est
    /// autorisé et que le cache n'a rien.
    /// </summary>
    Task<EnrichmentResult?> QueryAsync(EnrichmentQuery query, CancellationToken cancellationToken);
}
