using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Domain.Entities;

/// <summary>
/// Ensemble de <see cref="FileInstanceId"/> considérés comme des copies d'un même
/// contenu, selon un <see cref="DuplicateGroupKind"/>. En Inc 1, un groupe réunit
/// les instances partageant le même nom canonique et la même taille sur le volume.
/// </summary>
public sealed record DuplicateGroup(
    DuplicateGroupId Id,
    DuplicateGroupKind Kind,
    double Confidence,
    IReadOnlyList<FileInstanceId> Members)
{
    /// <summary>Un groupe n'est pertinent qu'à partir de deux instances.</summary>
    public bool IsActionable => Members.Count > 1;
}
