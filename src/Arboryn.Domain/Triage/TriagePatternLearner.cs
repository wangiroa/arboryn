using System.Text.RegularExpressions;

namespace Arboryn.Domain.Triage;

/// <summary>
/// Une correction utilisateur : sur un document donné, l'utilisateur a remplacé la valeur
/// pré-remplie d'un champ par la bonne. Sert de matière première à l'apprentissage.
/// </summary>
public sealed record TriageCorrection(
    TriagePatternKind Kind,
    string Snippet,
    string? ExtractedValue,
    string CorrectedValue);

/// <summary>
/// Apprentissage simple, sans ML : à partir des corrections utilisateur, dérive des patterns
/// génériques réutilisables. Le principe — une valeur corrigée saisie par l'utilisateur (par
/// ex. une source « Foncia », un objet « Appel de fonds ») est probablement présente
/// telle quelle dans les futurs documents du même émetteur/type ; on crée donc un pattern
/// littéral qui la reconnaîtra et la pré-remplira automatiquement, avec une priorité élevée.
/// </summary>
public static class TriagePatternLearner
{
    /// <summary>Priorité des patterns appris : au-dessus des patterns livrés (max 90).</summary>
    public const int LearnedPriority = 200;

    /// <summary>
    /// Dérive un pattern d'une correction, ou <c>null</c> si elle n'est pas exploitable
    /// (date — un littéral de date ne se généralise pas ; correction vide ; ou correction
    /// qui n'apporte rien de neuf par rapport à la valeur déjà extraite).
    /// </summary>
    public static TriagePattern? Derive(TriageCorrection correction)
    {
        if (correction.Kind == TriagePatternKind.Date)
        {
            return null;
        }

        var value = correction.CorrectedValue.Trim();
        if (value.Length < 2)
        {
            return null;
        }

        if (string.Equals(value, correction.ExtractedValue?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Motif littéral, insensible à la casse, borné par des frontières de mot quand la
        // valeur commence/finit par un caractère de mot.
        var escaped = Regex.Escape(value);
        var regex = $"{Boundary(value[0])}{escaped}{Boundary(value[^1])}";

        return new TriagePattern(
            Id: string.Empty,
            Kind: correction.Kind,
            Regex: regex,
            Template: value,
            Description: $"Appris : {value}",
            LearnedFromUser: true,
            Priority: LearnedPriority);
    }

    private static string Boundary(char c) => char.IsLetterOrDigit(c) ? @"\b" : string.Empty;
}
