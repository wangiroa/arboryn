namespace Arboryn.Domain.ValueObjects;

/// <summary>
/// Regroupe les <see cref="Entities.Operation"/> exécutées ensemble, pour permettre
/// l'annulation (undo) d'un lot entier en rejouant le journal à l'envers.
/// </summary>
public readonly record struct BatchId(string Value)
{
    public static BatchId New() => new(Guid.NewGuid().ToString("D"));
    public override string ToString() => Value;
}
