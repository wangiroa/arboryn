namespace Arboryn.Domain.ValueObjects;

public readonly record struct OperationId(string Value)
{
    public static OperationId New() => new(Guid.NewGuid().ToString("D"));
    public override string ToString() => Value;
}
