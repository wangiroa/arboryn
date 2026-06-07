using Arboryn.Domain.Triage;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Persistance du triage : patterns d'extraction (table <c>triage_patterns</c>) et corrections
/// utilisateur (table <c>triage_corrections</c>) qui alimentent l'apprentissage.
/// </summary>
public interface ITriageRepository
{
    /// <summary>
    /// Insère les patterns par défaut si la table est vide (idempotent). Renvoie le nombre
    /// de patterns insérés.
    /// </summary>
    Task<int> EnsureDefaultPatternsAsync(IReadOnlyList<TriagePattern> defaults, CancellationToken cancellationToken);

    /// <summary>Tous les patterns actifs, triés par priorité décroissante.</summary>
    Task<IReadOnlyList<TriagePattern>> GetActivePatternsAsync(CancellationToken cancellationToken);

    /// <summary>Ajoute un pattern et renvoie son identifiant assigné.</summary>
    Task<string> AddPatternAsync(TriagePattern pattern, CancellationToken cancellationToken);

    /// <summary>Vrai si un pattern actif équivalent (même type + même regex) existe déjà.</summary>
    Task<bool> PatternExistsAsync(TriagePatternKind kind, string regex, CancellationToken cancellationToken);

    /// <summary>Enregistre une correction utilisateur (non encore dérivée en pattern).</summary>
    Task AddCorrectionAsync(
        FileInstanceId? instanceId, TriageCorrection correction, CancellationToken cancellationToken);

    /// <summary>Corrections pas encore transformées en pattern (<c>derived_into_pattern_id IS NULL</c>).</summary>
    Task<IReadOnlyList<StoredCorrection>> GetUnderivedCorrectionsAsync(CancellationToken cancellationToken);

    /// <summary>Marque une correction comme dérivée vers le pattern indiqué.</summary>
    Task MarkCorrectionDerivedAsync(string correctionId, string patternId, CancellationToken cancellationToken);
}

/// <summary>Correction persistée, avec son identifiant, pour le job d'apprentissage.</summary>
public sealed record StoredCorrection(string Id, TriageCorrection Correction);
