namespace Arboryn.Domain.Triage;

/// <summary>Type de champ extrait par un pattern de triage.</summary>
public enum TriagePatternKind
{
    /// <summary>L'émetteur du document (banque, syndic, administration…).</summary>
    Source,

    /// <summary>La nature du document (Facture, Appel de fonds, Convocation AG…).</summary>
    Object,

    /// <summary>La date du document.</summary>
    Date,
}

/// <summary>
/// Règle d'extraction d'un champ de triage : une expression régulière .NET appliquée au
/// texte de la première page, et le <see cref="Template"/> qui en dérive la valeur (libellé
/// fixe, ou <c>$1</c>/<c>${name}</c> pour réinjecter un groupe capturé). Les patterns appris
/// des corrections utilisateur portent <see cref="LearnedFromUser"/> et une priorité élevée.
/// </summary>
public sealed record TriagePattern(
    string Id,
    TriagePatternKind Kind,
    string Regex,
    string? Template,
    string? Description,
    bool LearnedFromUser,
    int Priority,
    bool Active = true);
