namespace Arboryn.Application.Abstractions;

/// <summary>
/// Cache des réponses des providers d'enrichissement (table <c>api_cache</c>), clé par
/// (provider, hash de requête normalisée). Permet de servir un résultat sans nouvel appel
/// réseau — y compris en mode local-only — et d'atteindre le critère de taux de hit.
/// </summary>
public interface IApiCache
{
    /// <summary>Réponse JSON en cache (non expirée), ou <c>null</c> si absente/expirée.</summary>
    Task<string?> GetAsync(string provider, string queryHash, CancellationToken cancellationToken);

    /// <summary>Stocke la réponse JSON brute avec une durée de vie optionnelle.</summary>
    Task SetAsync(
        string provider, string queryHash, string responseJson, TimeSpan? timeToLive, CancellationToken cancellationToken);
}
