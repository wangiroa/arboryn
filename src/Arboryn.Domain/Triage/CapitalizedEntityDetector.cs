using System.Text.RegularExpressions;

namespace Arboryn.Domain.Triage;

/// <summary>
/// Détection heuristique de l'émetteur d'un document quand aucun pattern de source explicite
/// ne s'applique : on examine les premières lignes (l'en-tête contient presque toujours le
/// nom de l'expéditeur) et on retient la première qui ressemble à une raison sociale —
/// ligne en capitales, ou suite de mots capitalisés de longueur raisonnable.
/// </summary>
public static partial class CapitalizedEntityDetector
{
    private const int HeaderLineLimit = 8;
    private const int MinLength = 2;
    private const int MaxLength = 50;

    public static string? Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Take(HeaderLineLimit)
            .ToArray();

        // 1) Une ligne entièrement en capitales (ex. « FONCIA », « EDF SA »).
        foreach (var line in lines)
        {
            var cleaned = Trim(line);
            if (IsPlausible(cleaned) && IsAllCaps(cleaned))
            {
                return cleaned;
            }
        }

        // 2) À défaut, une ligne « Titre Capitalisé » (ex. « Cabinet Martin »).
        foreach (var line in lines)
        {
            var cleaned = Trim(line);
            if (IsPlausible(cleaned) && IsTitleCase(cleaned))
            {
                return cleaned;
            }
        }

        return null;
    }

    private static string Trim(string line) => line.Trim().Trim('-', ':', '*', '|', '.', ',').Trim();

    private static bool IsPlausible(string s)
        => s.Length is >= MinLength and <= MaxLength && LetterRegex().IsMatch(s);

    private static bool IsAllCaps(string s)
    {
        var letters = s.Where(char.IsLetter).ToArray();
        return letters.Length >= MinLength && letters.All(char.IsUpper);
    }

    private static bool IsTitleCase(string s)
    {
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Any(char.IsLetter))
            .ToArray();
        return words.Length is >= 1 and <= 5 && words.All(w => char.IsUpper(w[0]));
    }

    [GeneratedRegex(@"\p{L}")]
    private static partial Regex LetterRegex();
}
