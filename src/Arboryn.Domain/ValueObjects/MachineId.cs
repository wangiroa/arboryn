namespace Arboryn.Domain.ValueObjects;

/// <summary>
/// Identifiant stable d'une machine (PC) propriétaire de volumes. UUID v4 stocké en
/// chaîne. Introduit à l'Inc 13 pour nommer sans ambiguïté l'hôte d'un volume dans
/// un catalogue partagé entre plusieurs postes (PC hôte d'un volume).
/// </summary>
public readonly record struct MachineId(string Value)
{
    public static MachineId New() => new(Guid.NewGuid().ToString("D"));

    public override string ToString() => Value;
}
