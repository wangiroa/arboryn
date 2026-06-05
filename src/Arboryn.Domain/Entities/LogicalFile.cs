using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Domain.Entities;

/// <summary>
/// Œuvre ou document, indépendant de sa localisation. Identifié par sa signature
/// de contenu (<see cref="Signature"/>). Plusieurs <see cref="FileInstance"/> peuvent
/// référencer le même LogicalFile (copies sur le même volume ou réparties).
/// </summary>
public sealed record LogicalFile(
    LogicalFileId Id,
    MediaCategory Category,
    ContentSignature Signature,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? CanonicalPath = null,
    string? CanonicalFilename = null);
