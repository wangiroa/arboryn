using System.Text.Json;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enrichment;
using Arboryn.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Arboryn.Infrastructure.Enrichment;

/// <summary>
/// Base des providers HTTP/JSON. Centralise l'appel réseau tolérant (GET → JsonDocument, erreurs
/// isolées) et l'échappement d'URL. <em>Privacy-first</em> : les classes dérivées ne doivent
/// construire l'URL qu'à partir de <see cref="EnrichmentQuery.Get"/> (champs en liste blanche).
/// </summary>
public abstract class HttpMetadataProvider : IMetadataProvider
{
    protected HttpMetadataProvider(HttpClient http, ILogger logger)
    {
        Http = http;
        Logger = logger;
    }

    protected HttpClient Http { get; }

    protected ILogger Logger { get; }

    public abstract string Name { get; }

    public abstract bool CanEnrich(MediaCategory category);

    public virtual bool IsConfigured => true;

    public abstract Task<EnrichmentResult?> QueryAsync(EnrichmentQuery query, CancellationToken cancellationToken);

    /// <summary>GET tolérant renvoyant un <see cref="JsonDocument"/>, ou <c>null</c> en cas d'échec.</summary>
    protected async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogDebug("{Provider} : {Url} → {Status}", Name, url, response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Logger.LogWarning(ex, "{Provider} : échec de la requête.", Name);
            return null;
        }
    }

    protected static string Escape(string value) => Uri.EscapeDataString(value);

    /// <summary>Extrait une année plausible (4 chiffres) du début d'une date (« 2001-05-01 », « 2001 »).</summary>
    protected static string? Year(string? date)
    {
        if (string.IsNullOrWhiteSpace(date) || date.Length < 4)
        {
            return null;
        }

        var head = date.AsSpan(0, 4);
        foreach (var c in head)
        {
            if (!char.IsDigit(c))
            {
                return null;
            }
        }

        return head.ToString();
    }

    /// <summary>Lit une propriété chaîne si présente et non vide.</summary>
    protected static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
