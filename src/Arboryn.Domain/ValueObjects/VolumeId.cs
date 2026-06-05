namespace Arboryn.Domain.ValueObjects;

/// <summary>
/// Identifiant stable d'un Volume. UUID v4 stocké en chaîne.
/// </summary>
public readonly record struct VolumeId(string Value)
{
    public static VolumeId Default { get; } = new("00000000-0000-0000-0000-000000000000");

    public static VolumeId New() => new(Guid.NewGuid().ToString("D"));

    public override string ToString() => Value;
}
