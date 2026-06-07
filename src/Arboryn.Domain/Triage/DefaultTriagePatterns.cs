namespace Arboryn.Domain.Triage;

/// <summary>
/// Patterns de triage livrés par défaut pour les cas français courants. Servis tant que la
/// table <c>triage_patterns</c> est vide ; ils sont ensuite complétés par les patterns appris
/// des corrections utilisateur. Les <see cref="TriagePattern.Id"/> sont vides : le dépôt en
/// assigne un à l'insertion. Plus la priorité est haute, plus le pattern est essayé tôt.
/// </summary>
public static class DefaultTriagePatterns
{
    public static IReadOnlyList<TriagePattern> All { get; } = new[]
    {
        // --- Objet (nature du document) : du plus spécifique au plus générique. ---
        Object(@"convocation[\s\S]{0,40}?(assembl[ée]e\s+g[ée]n[ée]rale|\bAG\b)", "Convocation AG", "Convocation à l'assemblée générale", 90),
        Object(@"proc[èe]s[\-\s]verbal|\bPV\b[\s\S]{0,20}?\bAG\b", "Procès-verbal AG", "Procès-verbal d'assemblée générale", 90),
        Object(@"appel\s+de\s+fonds?", "Appel de fonds", "Appel de fonds de copropriété", 85),
        Object(@"bulletin\s+de\s+(paie|salaire)", "Bulletin de paie", "Bulletin de paie / salaire", 80),
        Object(@"avis\s+d['e]\s*[ée]ch[ée]ance", "Avis d'échéance", "Avis d'échéance", 80),
        Object(@"taxe\s+(fonci[èe]re|d['e]habitation)", "Taxe", "Taxe foncière ou d'habitation", 80),
        Object(@"\bquittance\b", "Quittance", "Quittance de loyer", 70),
        Object(@"\bfactures?\b", "Facture", "Facture", 60),
        Object(@"\bdevis\b", "Devis", "Devis", 60),
        Object(@"\bcontrat\b", "Contrat", "Contrat", 60),
        Object(@"\battestations?\b", "Attestation", "Attestation", 55),
        Object(@"relev[ée]\s+(de\s+compte|bancaire)", "Relevé de compte", "Relevé de compte bancaire", 55),
        Object(@"\brelev[ée]s?\b", "Relevé", "Relevé", 40),
        Object(@"\bavis\b", "Avis", "Avis", 30),

        // --- Source (émetteur) : motifs d'en-tête explicites. La détection d'entité
        //     capitalisée prend le relais quand aucun de ces motifs ne s'applique. ---
        Source(@"[ée]mis\s+par\s*:?\s*(?<v>[^\r\n]+)", "${v}", "Mention « émis par »", 70),
        Source(@"exp[ée]diteur\s*:?\s*(?<v>[^\r\n]+)", "${v}", "Mention « expéditeur »", 70),
        Source(@"soci[ée]t[ée]\s+(?<v>[A-ZÀ-Ý][^\r\n]{1,40})", "${v}", "Mention « société … »", 50),

        // --- Date : le repérage fin est délégué à FrenchDateParser ; ces motifs explicites
        //     captent les libellés courants pour privilégier la date du document. ---
        Date(@"(?:fait\s+le|en\s+date\s+du|le)\s+(?<v>\d{1,2}\s+[A-Za-zà-ÿ]{3,9}\s+\d{4})", "${v}", "Date littérale précédée d'un libellé", 70),
        Date(@"(?:du|le|date)\s*:?\s*(?<v>\d{1,2}[/.\-]\d{1,2}[/.\-]\d{2,4})", "${v}", "Date numérique précédée d'un libellé", 65),
    };

    private static TriagePattern Object(string regex, string template, string description, int priority)
        => new(string.Empty, TriagePatternKind.Object, regex, template, description, LearnedFromUser: false, priority);

    private static TriagePattern Source(string regex, string template, string description, int priority)
        => new(string.Empty, TriagePatternKind.Source, regex, template, description, LearnedFromUser: false, priority);

    private static TriagePattern Date(string regex, string template, string description, int priority)
        => new(string.Empty, TriagePatternKind.Date, regex, template, description, LearnedFromUser: false, priority);
}
