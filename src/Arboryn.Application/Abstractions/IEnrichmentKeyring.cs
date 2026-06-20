namespace Arboryn.Application.Abstractions;

/// <summary>
/// Trousseau de clés d'API des providers d'enrichissement (TMDB obligatoire, Google Books
/// optionnelle). Les clés sont saisies dans les Settings et persistées (table <c>settings</c>,
/// clé <c>api_key_&lt;provider&gt;</c>) ; ce trousseau en garde un instantané en mémoire pour un
/// accès synchrone par les providers. <see cref="RefreshAsync"/> est appelé au démarrage et
/// après modification dans l'UI.
/// </summary>
public interface IEnrichmentKeyring
{
    /// <summary>Clé d'API du provider, ou <c>null</c> si non configurée.</summary>
    string? ApiKey(string provider);

    /// <summary>Recharge l'instantané des clés depuis le stockage des réglages.</summary>
    Task RefreshAsync(CancellationToken cancellationToken);
}
