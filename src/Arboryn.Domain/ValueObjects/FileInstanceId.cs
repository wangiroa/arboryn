namespace Arboryn.Domain.ValueObjects;

public readonly record struct FileInstanceId(string Value)
{
    public static FileInstanceId New() => new(Guid.NewGuid().ToString("D"));
    public override string ToString() => Value;
}
