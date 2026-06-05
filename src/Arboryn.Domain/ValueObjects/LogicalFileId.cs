namespace Arboryn.Domain.ValueObjects;

public readonly record struct LogicalFileId(string Value)
{
    public static LogicalFileId New() => new(Guid.NewGuid().ToString("D"));
    public override string ToString() => Value;
}
