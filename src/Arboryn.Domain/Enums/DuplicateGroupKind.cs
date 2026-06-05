namespace Arboryn.Domain.Enums;

/// <summary>
/// Critère ayant servi à regrouper des copies. Seul <see cref="ExactName"/> est
/// produit en Inc 1 ; les autres arrivent aux incréments suivants (flou, hash,
/// perceptuel) mais sont définis dès maintenant pour figer la persistance.
/// </summary>
public enum DuplicateGroupKind
{
    /// <summary>Même nom canonique et même taille (Inc 1).</summary>
    ExactName,

    /// <summary>Noms proches (Levenshtein / Jaccard) — Inc 2.</summary>
    FuzzyName,

    /// <summary>Contenu binaire identique (SHA-256) — Inc 2.</summary>
    ExactHash,

    /// <summary>Contenu perceptuellement équivalent (pHash / Chromaprint) — Inc 5.</summary>
    Perceptual
}
