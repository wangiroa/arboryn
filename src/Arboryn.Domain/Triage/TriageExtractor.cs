using System.Text.RegularExpressions;

namespace Arboryn.Domain.Triage;

/// <summary>Valeur pré-remplie d'un champ de triage, avec sa confiance et le motif l'ayant produite.</summary>
public sealed record TriageField(string? Value, double Confidence, string? MatchedBy = null)
{
    public static readonly TriageField Empty = new(null, 0.0);

    public bool HasValue => !string.IsNullOrWhiteSpace(Value);
}

/// <summary>Résultat du pré-remplissage des trois champs de triage d'un document.</summary>
public sealed record TriageExtraction(TriageField Source, TriageField Object, TriageField Date);

/// <summary>
/// Applique les patterns de triage (livrés + appris) au texte de la première page d'un
/// document pour pré-remplir les trois champs : source (émetteur), objet (nature), date.
/// Les patterns sont essayés par priorité décroissante ; la date passe par
/// <see cref="FrenchDateParser"/> pour la normalisation en <c>yyyyMM</c>. Pur et testable.
/// </summary>
public static class TriageExtractor
{
    // Confiances indicatives selon l'origine de la valeur.
    private const double LearnedConfidence = 0.92;
    private const double PatternConfidence = 0.8;
    private const double DateGenericConfidence = 0.6;
    private const double EntityFallbackConfidence = 0.4;

    // Garde-fou contre un pattern utilisateur pathologique (ReDoS) : timeout par essai.
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    public static TriageExtraction Extract(string? text, IReadOnlyList<TriagePattern> patterns)
    {
        var byKind = patterns
            .Where(p => p.Active)
            .GroupBy(p => p.Kind)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Priority).ToList());

        return new TriageExtraction(
            Source: ExtractField(text, byKind, TriagePatternKind.Source) ?? DetectSource(text),
            Object: ExtractField(text, byKind, TriagePatternKind.Object) ?? TriageField.Empty,
            Date: ExtractDate(text, byKind));
    }

    private static TriageField? ExtractField(
        string? text, IReadOnlyDictionary<TriagePatternKind, List<TriagePattern>> byKind, TriagePatternKind kind)
    {
        if (string.IsNullOrWhiteSpace(text) || !byKind.TryGetValue(kind, out var list))
        {
            return null;
        }

        foreach (var pattern in list)
        {
            if (TryApply(text, pattern, out var value))
            {
                var confidence = pattern.LearnedFromUser ? LearnedConfidence : PatternConfidence;
                return new TriageField(value, confidence, pattern.Description ?? pattern.Regex);
            }
        }

        return null;
    }

    private static TriageField ExtractDate(
        string? text, IReadOnlyDictionary<TriagePatternKind, List<TriagePattern>> byKind)
    {
        // 1) Motifs de date explicites (libellés « fait le », « en date du »…) → normalisation.
        if (!string.IsNullOrWhiteSpace(text) && byKind.TryGetValue(TriagePatternKind.Date, out var list))
        {
            foreach (var pattern in list)
            {
                if (TryApply(text, pattern, out var raw) && FrenchDateParser.TryParse(raw, out var yyyyMM))
                {
                    var confidence = pattern.LearnedFromUser ? LearnedConfidence : PatternConfidence;
                    return new TriageField(yyyyMM, confidence, pattern.Description ?? pattern.Regex);
                }
            }
        }

        // 2) Balayage générique : première date reconnue dans le texte.
        return FrenchDateParser.ScanFirst(text, out var scanned)
            ? new TriageField(scanned, DateGenericConfidence, "FrenchDateParser")
            : TriageField.Empty;
    }

    private static TriageField DetectSource(string? text)
    {
        var entity = CapitalizedEntityDetector.Detect(text);
        return entity is null
            ? TriageField.Empty
            : new TriageField(entity, EntityFallbackConfidence, "CapitalizedEntityDetector");
    }

    /// <summary>
    /// Applique un pattern et rend la valeur dérivée du <see cref="TriagePattern.Template"/>
    /// ($1, ${name}) ou, à défaut de template, le texte capturé. Les patterns invalides ou
    /// trop lents sont ignorés sans planter le triage.
    /// </summary>
    private static bool TryApply(string text, TriagePattern pattern, out string value)
    {
        value = string.Empty;
        try
        {
            var match = Regex.Match(text, pattern.Regex,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
            if (!match.Success)
            {
                return false;
            }

            var rendered = string.IsNullOrEmpty(pattern.Template)
                ? (match.Groups.Count > 1 ? match.Groups[1].Value : match.Value)
                : match.Result(pattern.Template);

            value = Normalize(rendered);
            return value.Length > 0;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // Regex ou template malformé (pattern appris corrompu) : on l'ignore.
            return false;
        }
    }

    private static string Normalize(string s)
        => Regex.Replace(s, @"\s+", " ").Trim().Trim('-', ':', '*', '|', '.', ',').Trim();
}
