using Arboryn.Domain.Enums;

namespace Arboryn.Domain.ValueObjects;

/// <summary>
/// Signature de contenu d'un <see cref="Entities.LogicalFile"/> : couple
/// (<see cref="ContentSignatureKind"/>, <see cref="Value"/>) où Value est la forme
/// textuelle dépendante du type (nom|taille, hex SHA-256, etc.).
/// </summary>
public readonly record struct ContentSignature(ContentSignatureKind Kind, string Value)
{
    /// <summary>Signature « nom canonique + taille », utilisée tant que le hash n'est pas connu.</summary>
    public static ContentSignature NameSize(CanonicalName canonicalName, long size)
        => new(ContentSignatureKind.NameSize, $"{canonicalName.Value}|{size}");

    /// <summary>Signature SHA-256 — identité de contenu confirmée.</summary>
    public static ContentSignature FromSha256(Sha256 hash)
        => new(ContentSignatureKind.Sha256, hash.Value);

    /// <summary>
    /// Signature perceptuelle — identité d'un groupe de copies visuellement proches.
    /// La valeur est l'empreinte hexadécimale du représentant du groupe.
    /// </summary>
    public static ContentSignature FromPerceptualHash(PerceptualHash hash)
        => new(ContentSignatureKind.PHash, hash.ToHex());

    public override string ToString() => $"{Kind}:{Value}";
}
