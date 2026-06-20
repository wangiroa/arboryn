using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Arboryn.Domain.Enums;

namespace Arboryn.Domain.Enrichment;

/// <summary>
/// Requête d'enrichissement <em>privacy-first</em> : ne porte que des champs structurés issus
/// de l'extraction locale (titre nettoyé, auteur, ISBN, année…), jamais le nom de fichier ni
/// le chemin brut. C'est l'unique objet transmis aux providers : la garantie de confidentialité
/// repose sur le fait que rien d'autre n'en sort. Fournit une forme canonique stable
/// (normalisée, triée) et une clé de cache déterministe.
/// </summary>
public sealed record EnrichmentQuery(MediaCategory Category, IReadOnlyDictionary<string, string> Fields)
{
    /// <summary>Vrai si la requête ne porte aucun champ exploitable (rien à demander).</summary>
    public bool IsEmpty => Fields.Count == 0 || Fields.All(kv => string.IsNullOrWhiteSpace(kv.Value));

    /// <summary>Accès direct à un champ normalisé, ou <c>null</c> s'il est absent/vide.</summary>
    public string? Get(string key)
        => Fields.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    /// <summary>
    /// Forme canonique : champs non vides, normalisés (minuscule, sans accents, espaces
    /// réduits), triés par clé et joints. Indépendante du provider — base de la clé de cache.
    /// </summary>
    public string CanonicalForm()
        => string.Join("&", Fields
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => new KeyValuePair<string, string>(kv.Key, Normalize(kv.Value)))
            .Where(kv => kv.Value.Length > 0)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));

    /// <summary>Clé de cache déterministe (SHA-256 hex) de la catégorie + forme canonique.</summary>
    public string CacheKey()
    {
        var payload = $"{(int)Category}|{CanonicalForm()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Normalisation de valeur : casse, accents, espaces — pour stabiliser le cache.</summary>
    public static string Normalize(string value)
    {
        var lowered = value.Trim().ToLowerInvariant();
        var decomposed = lowered.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            sb.Append(ch);
        }

        return sb.ToString().Trim().Normalize(NormalizationForm.FormC);
    }
}
