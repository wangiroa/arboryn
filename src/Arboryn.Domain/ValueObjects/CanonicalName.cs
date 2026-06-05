using System.Globalization;
using System.Text;

namespace Arboryn.Domain.ValueObjects;

/// <summary>
/// Nom de fichier normalisé utilisé pour la détection rapide de doublons.
/// Applique : lowercase, suppression d'accents, normalisation des séparateurs,
/// suppression des suffixes de copie courants.
/// </summary>
public readonly record struct CanonicalName(string Value)
{
    public override string ToString() => Value;

    public static CanonicalName From(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new CanonicalName(string.Empty);
        }

        var s = fileName.ToLowerInvariant();

        // Retire l'extension (préservée séparément)
        var extIdx = s.LastIndexOf('.');
        var ext = extIdx > 0 ? s[extIdx..] : string.Empty;
        var name = extIdx > 0 ? s[..extIdx] : s;

        // Retire les accents
        name = RemoveDiacritics(name);

        // Retire les suffixes de copie courants : (1), (2), - copy, - copie, _copy, etc.
        // Les chiffres NON-parenthésés en fin de nom (DSC_123, IMG_45, rapport 2020) sont
        // CONSERVÉS — ce sont des numéros de séquence ou des années, pas des marqueurs de copie.
        name = System.Text.RegularExpressions.Regex.Replace(
            name,
            @"\s*[-_]?\s*\(\d+\)$|\s*[-_]?\s*(copy|copie)\s*\d*$|\s*[-_]?\s*(copy|copie)\s*\(\d+\)$",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Normalise les séparateurs et la ponctuation
        name = System.Text.RegularExpressions.Regex.Replace(name, @"[\s\-_.]+", " ").Trim();

        return new CanonicalName(name + ext);
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
