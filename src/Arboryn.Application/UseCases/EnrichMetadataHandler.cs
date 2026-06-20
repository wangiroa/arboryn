using System.Globalization;
using System.Text.Json;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enrichment;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Enrichit les métadonnées d'un fichier via les providers en ligne, <em>privacy-first</em> :
/// construit une requête à partir des seuls champs structurés (liste blanche), interroge le
/// cache puis — si le mode en ligne est autorisé — les providers de la catégorie par priorité.
/// Les champs dont la confiance dépasse le seuil sont auto-appliqués (écrits en
/// <c>file_metadata</c>, source <c>online_&lt;provider&gt;</c>) ; les autres sont renvoyés
/// comme candidats à valider par l'utilisateur. Aucun appel réseau en mode local-only.
/// </summary>
public sealed class EnrichMetadataHandler
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(30);
    private const double DefaultThreshold = 0.9;

    private readonly IReadOnlyList<IMetadataProvider> _providers;
    private readonly IApiCache _cache;
    private readonly IFileMetadataRepository _metadata;
    private readonly IEnrichmentCandidateRepository _candidates;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<EnrichMetadataHandler> _logger;

    public EnrichMetadataHandler(
        IEnumerable<IMetadataProvider> providers,
        IApiCache cache,
        IFileMetadataRepository metadata,
        IEnrichmentCandidateRepository candidates,
        ISettingsRepository settings,
        ILogger<EnrichMetadataHandler> logger)
    {
        _providers = providers.ToList();
        _cache = cache;
        _metadata = metadata;
        _candidates = candidates;
        _settings = settings;
        _logger = logger;
    }

    public async Task<EnrichmentOutcome> ExecuteAsync(
        FileInstanceId instanceId, MediaCategory category, CancellationToken cancellationToken = default)
    {
        var fused = await _metadata.GetFusedAsync(instanceId, cancellationToken).ConfigureAwait(false);
        var values = fused
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Value!, StringComparer.Ordinal);

        var query = EnrichmentQueryBuilder.Build(category, values);
        if (query.IsEmpty)
        {
            return new EnrichmentOutcome(instanceId, 0, Array.Empty<EnrichmentCandidate>(), NetworkUsed: false);
        }

        var networkAllowed = await IsOnlineAllowedAsync(category, cancellationToken).ConfigureAwait(false);
        var threshold = await GetThresholdAsync(cancellationToken).ConfigureAwait(false);

        var applied = 0;
        var candidates = new List<EnrichmentCandidate>();
        var networkUsed = false;
        var now = DateTime.UtcNow;
        var hash = query.CacheKey();

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!provider.CanEnrich(category) || !provider.IsConfigured)
            {
                continue;
            }

            var (result, usedNetwork) = await ResolveAsync(provider, query, hash, networkAllowed, cancellationToken)
                .ConfigureAwait(false);
            networkUsed |= usedNetwork;

            if (result is not { HasFields: true })
            {
                continue;
            }

            foreach (var (key, value) in result.Fields)
            {
                if (result.Confidence >= threshold)
                {
                    await _metadata.UpsertAsync(new MetadataEntry(
                        instanceId, key, value, MetadataSources.Online(provider.Name), result.Confidence, now),
                        cancellationToken).ConfigureAwait(false);
                    applied++;
                }
                else
                {
                    candidates.Add(new EnrichmentCandidate(provider.Name, key, value, result.Confidence));

                    // Persiste le candidat pour la révision utilisateur (accepter / rejeter).
                    await _candidates.UpsertAsync(new EnrichmentCandidateRecord(
                        Guid.NewGuid().ToString(), instanceId, provider.Name, key, value,
                        result.Confidence, EnrichmentCandidateStatus.Pending),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            // Appariement par identifiant exact : inutile d'interroger les providers de repli.
            if (result.Match == EnrichmentMatchKind.Identifier)
            {
                break;
            }
        }

        _logger.LogInformation(
            "Enrichissement de {Instance} ({Category}) : {Applied} champ(s) appliqué(s), {Candidates} candidat(s), réseau={Network}.",
            instanceId.Value, category, applied, candidates.Count, networkUsed);

        return new EnrichmentOutcome(instanceId, applied, candidates, networkUsed);
    }

    /// <summary>Sert le résultat depuis le cache, ou interroge le provider si le réseau est permis.</summary>
    private async Task<(EnrichmentResult? Result, bool UsedNetwork)> ResolveAsync(
        IMetadataProvider provider, EnrichmentQuery query, string hash, bool networkAllowed, CancellationToken cancellationToken)
    {
        var cached = await _cache.GetAsync(provider.Name, hash, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return (Deserialize(cached), false);
        }

        if (!networkAllowed)
        {
            return (null, false);
        }

        EnrichmentResult? result;
        try
        {
            result = await provider.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider {Provider} indisponible.", provider.Name);
            return (null, true);
        }

        // Cache la réponse (y compris « aucun résultat ») pour éviter de réinterroger.
        await _cache.SetAsync(
            provider.Name, hash,
            Serialize(result ?? new EnrichmentResult(provider.Name, EmptyFields, 0, EnrichmentMatchKind.Fuzzy)),
            CacheTtl, cancellationToken).ConfigureAwait(false);

        return (result, true);
    }

    private async Task<bool> IsOnlineAllowedAsync(MediaCategory category, CancellationToken cancellationToken)
    {
        var global = await ReadBoolAsync("online_mode_enabled", false, cancellationToken).ConfigureAwait(false);
        if (!global)
        {
            return false;
        }

        // Surcharge par catégorie : peut désactiver une catégorie quand le mode global est actif.
        var perCategory = await _settings
            .GetAsync($"online_mode_{LogicalFileEnumsKey(category)}", cancellationToken).ConfigureAwait(false);
        return perCategory is null || !string.Equals(perCategory, "false", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<double> GetThresholdAsync(CancellationToken cancellationToken)
    {
        var raw = await _settings.GetAsync("confidence_auto_apply", cancellationToken).ConfigureAwait(false);
        return raw is not null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : DefaultThreshold;
    }

    private async Task<bool> ReadBoolAsync(string key, bool fallback, CancellationToken cancellationToken)
    {
        var raw = await _settings.GetAsync(key, cancellationToken).ConfigureAwait(false);
        return raw is null ? fallback : string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }

    // Clé de catégorie pour les réglages par-catégorie (aligne sur la sérialisation DB).
    internal static string LogicalFileEnumsKey(MediaCategory category) => category switch
    {
        MediaCategory.Audiobook => "audiobook",
        MediaCategory.Book => "book",
        MediaCategory.Comic => "comic",
        MediaCategory.Video => "video",
        MediaCategory.Photo => "photo",
        MediaCategory.OfficialDocument => "official_document",
        MediaCategory.OtherDocument => "other_document",
        _ => "unknown",
    };

    private static readonly IReadOnlyDictionary<string, string> EmptyFields =
        new Dictionary<string, string>();

    private static string Serialize(EnrichmentResult result)
        => JsonSerializer.Serialize(new CachedResult(
            result.Provider, new Dictionary<string, string>(result.Fields), result.Confidence, result.Match));

    private static EnrichmentResult? Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize<CachedResult>(json);
        return dto is null
            ? null
            : new EnrichmentResult(dto.Provider, dto.Fields, dto.Confidence, dto.Match);
    }

    private sealed record CachedResult(
        string Provider, Dictionary<string, string> Fields, double Confidence, EnrichmentMatchKind Match);
}

/// <summary>Résultat de l'enrichissement d'un fichier.</summary>
public sealed record EnrichmentOutcome(
    FileInstanceId InstanceId,
    int AppliedFields,
    IReadOnlyList<EnrichmentCandidate> Candidates,
    bool NetworkUsed);

/// <summary>Un champ enrichi proposé mais non auto-appliqué (confiance sous le seuil).</summary>
public sealed record EnrichmentCandidate(string Provider, string Key, string Value, double Confidence);
