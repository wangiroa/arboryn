namespace Arboryn.Domain.ValueObjects;

/// <summary>Identifiant stable d'un <c>ReplicationScope</c> (Inc 10).</summary>
public readonly record struct ScopeId(string Value)
{
    public static ScopeId New() => new(Guid.NewGuid().ToString("D"));
    public override string ToString() => Value;
}
