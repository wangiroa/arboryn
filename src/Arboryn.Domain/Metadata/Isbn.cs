using System.Text.RegularExpressions;

namespace Arboryn.Domain.Metadata;

/// <summary>
/// Détection et normalisation d'un ISBN-10 / ISBN-13 dans une chaîne libre
/// (identifiant OPF, sujet ou mots-clés d'un PDF…). Pure et déterministe.
/// </summary>
public static partial class Isbn
{
    /// <summary>
    /// Cherche un ISBN plausible dans <paramref name="text"/>. En cas de succès,
    /// <paramref name="normalized"/> contient les chiffres (et un éventuel « X » final)
    /// sans tirets ni espaces.
    /// </summary>
    public static bool TryExtract(string? text, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = IsbnRegex().Match(text);
        if (!match.Success)
        {
            return false;
        }

        normalized = new string(match.Value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return true;
    }

    // ISBN-10 / ISBN-13, avec tirets/espaces optionnels et préfixe « 978/979 » éventuel.
    [GeneratedRegex(@"\b(?:97[89][-\s]?)?(?:\d[-\s]?){9}[\dXx]\b")]
    private static partial Regex IsbnRegex();
}
