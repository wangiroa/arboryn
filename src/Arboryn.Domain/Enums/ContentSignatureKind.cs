namespace Arboryn.Domain.Enums;

/// <summary>
/// Type de signature de contenu identifiant un <see cref="Entities.LogicalFile"/>.
/// Les valeurs correspondent à la contrainte CHECK de la colonne
/// <c>logical_files.content_signature_kind</c>.
/// </summary>
public enum ContentSignatureKind
{
    /// <summary>Nom canonique + taille (Inc 1, fallback tant que le hash n'est pas calculé).</summary>
    NameSize,

    /// <summary>Empreinte SHA-256 du contenu — identité exacte (Inc 2).</summary>
    Sha256,

    /// <summary>Empreinte perceptuelle d'image (pHash) — Inc 5.</summary>
    PHash,

    /// <summary>Empreinte acoustique (Chromaprint) — Inc 5.</summary>
    Chromaprint,
}
