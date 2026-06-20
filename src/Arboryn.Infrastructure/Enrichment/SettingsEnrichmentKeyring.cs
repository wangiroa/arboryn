using System.Collections.Concurrent;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enrichment;

namespace Arboryn.Infrastructure.Enrichment;

/// <summary>
/// Trousseau de clés alimenté par la table <c>settings</c> (clé <c>api_key_&lt;provider&gt;</c>).
/// Singleton : conserve un instantané en mémoire pour un accès synchrone par les providers,
/// rechargé au démarrage et après sauvegarde dans l'UI Settings.
/// </summary>
public sealed class SettingsEnrichmentKeyring : IEnrichmentKeyring
{
    private static readonly string[] Providers =
    {
        ProviderNames.OpenLibrary, ProviderNames.GoogleBooks, ProviderNames.Tmdb, ProviderNames.MusicBrainz,
    };

    private readonly ISettingsRepository _settings;
    private readonly ConcurrentDictionary<string, string> _keys = new(StringComparer.Ordinal);

    public SettingsEnrichmentKeyring(ISettingsRepository settings)
        => _settings = settings;

    public string? ApiKey(string provider)
        => _keys.TryGetValue(provider, out var key) && !string.IsNullOrWhiteSpace(key) ? key : null;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        foreach (var provider in Providers)
        {
            var value = await _settings.GetAsync($"api_key_{provider}", cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(value))
            {
                _keys.TryRemove(provider, out _);
            }
            else
            {
                _keys[provider] = value.Trim();
            }
        }
    }
}
