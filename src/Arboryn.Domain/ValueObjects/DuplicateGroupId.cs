namespace Arboryn.Domain.ValueObjects;

public readonly record struct DuplicateGroupId(string Value)
{
    public static DuplicateGroupId New() => new(Guid.NewGuid().ToString("D"));
    public override string ToString() => Value;
}
