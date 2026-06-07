using System.Globalization;
using System.Text.RegularExpressions;

namespace Arboryn.Domain.Triage;

/// <summary>
/// Reconnaît et normalise les dates écrites en français au format canonique <c>yyyyMM</c>
/// (le format retenu pour le nommage des documents officiels). Gère les formes numériques
/// (JJ/MM/AAAA, AAAA-MM-JJ, MM/AAAA), littérales (« 12 mars 2024 », « mars 2024 ») et
/// abrégées (MMAA). Pur, sans I/O — testable.
/// </summary>
public static partial class FrenchDateParser
{
    // Noms de mois français → numéro. Inclut les abréviations et les variantes sans accent.
    private static readonly IReadOnlyDictionary<string, int> Months = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["janvier"] = 1, ["janv"] = 1, ["jan"] = 1,
        ["février"] = 2, ["fevrier"] = 2, ["févr"] = 2, ["fevr"] = 2, ["fév"] = 2, ["fev"] = 2,
        ["mars"] = 3, ["mar"] = 3,
        ["avril"] = 4, ["avr"] = 4,
        ["mai"] = 5,
        ["juin"] = 6,
        ["juillet"] = 7, ["juil"] = 7, ["juill"] = 7,
        ["août"] = 8, ["aout"] = 8,
        ["septembre"] = 9, ["sept"] = 9, ["sep"] = 9,
        ["octobre"] = 10, ["oct"] = 10,
        ["novembre"] = 11, ["nov"] = 11,
        ["décembre"] = 12, ["decembre"] = 12, ["déc"] = 12, ["dec"] = 12,
    };

    private const int MinYear = 1900;
    private const int MaxYear = 2099;

    /// <summary>
    /// Tente d'interpréter une chaîne unique (déjà isolée) comme une date et de la
    /// normaliser en <c>yyyyMM</c>. Renvoie <c>false</c> si rien d'exploitable.
    /// </summary>
    public static bool TryParse(string? value, out string yearMonth)
    {
        yearMonth = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();

        // AAAA-MM-JJ (ISO) ou AAAA/MM
        if (IsoRegex().Match(text) is { Success: true } iso)
        {
            return Build(iso.Groups["y"].Value, iso.Groups["m"].Value, out yearMonth);
        }

        // JJ/MM/AAAA ou MM/AAAA (séparateurs / . -). L'année est toujours le groupe « a »,
        // le mois le groupe « b » ; le jour optionnel (« c ») n'intervient pas dans yyyyMM.
        if (NumericRegex().Match(text) is { Success: true } num)
        {
            return Build(num.Groups["a"].Value, num.Groups["b"].Value, out yearMonth);
        }

        // « 12 mars 2024 » ou « mars 2024 »
        if (LiteralRegex().Match(text) is { Success: true } lit
            && Months.TryGetValue(lit.Groups["mon"].Value, out var month))
        {
            return Build(lit.Groups["y"].Value, month.ToString(CultureInfo.InvariantCulture), out yearMonth);
        }

        // MMAA accolé (ex. « 0324 » → mars 2024). Ambigu : on n'accepte que 4 chiffres
        // dont les deux premiers forment un mois valide.
        if (CompactRegex().Match(text) is { Success: true } compact)
        {
            var mm = compact.Groups["m"].Value;
            var yy = compact.Groups["y"].Value;
            if (int.TryParse(mm, out var m) && m is >= 1 and <= 12)
            {
                return Build("20" + yy, mm, out yearMonth);
            }
        }

        return false;
    }

    /// <summary>
    /// Parcourt un texte libre (première page d'un document) et renvoie la première date
    /// reconnue, normalisée en <c>yyyyMM</c>. Les formes littérales et complètes sont
    /// privilégiées sur les formes compactes ambiguës.
    /// </summary>
    public static bool ScanFirst(string? text, out string yearMonth)
    {
        yearMonth = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var regex in new[] { IsoRegex(), NumericRegex(), LiteralRegex() })
        {
            foreach (Match match in regex.Matches(text))
            {
                if (TryParse(match.Value, out yearMonth))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Build(string yearText, string monthText, out string yearMonth)
    {
        yearMonth = string.Empty;
        if (!int.TryParse(yearText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(monthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month))
        {
            return false;
        }

        if (year is < MinYear or > MaxYear || month is < 1 or > 12)
        {
            return false;
        }

        yearMonth = $"{year:0000}{month:00}";
        return true;
    }

    // AAAA-MM(-JJ) — ancre sur une année 19xx/20xx pour éviter de capter JJ/MM/AAAA à l'envers.
    [GeneratedRegex(@"\b(?<y>(?:19|20)\d{2})[/.\-](?<m>\d{1,2})(?:[/.\-]\d{1,2})?\b")]
    private static partial Regex IsoRegex();

    // (JJ sep)? MM sep AAAA. Le dernier groupe est l'année.
    [GeneratedRegex(@"\b(?:(?<c>\d{1,2})[/.\-])?(?<b>\d{1,2})[/.\-](?<a>(?:19|20)\d{2})\b")]
    private static partial Regex NumericRegex();

    // (JJ )? mois AAAA
    [GeneratedRegex(@"\b(?:\d{1,2}\s+)?(?<mon>[A-Za-zàâäéèêëîïôöùûüçÀÂÄÉÈÊËÎÏÔÖÙÛÜÇ]{3,9})\.?\s+(?<y>(?:19|20)\d{2})\b")]
    private static partial Regex LiteralRegex();

    [GeneratedRegex(@"^(?<m>\d{2})(?<y>\d{2})$")]
    private static partial Regex CompactRegex();
}
