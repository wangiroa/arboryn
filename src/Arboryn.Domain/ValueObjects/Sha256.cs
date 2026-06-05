namespace Arboryn.Domain.ValueObjects;

/// <summary>
/// Empreinte SHA-256 d'un contenu de fichier, stockée en hexadécimal minuscule (64 caractères).
/// </summary>
public readonly record struct Sha256
{
    public string Value { get; }

    private Sha256(string value) => Value = value;

    /// <summary>Construit à partir des 32 octets bruts du condensé.</summary>
    /// <exception cref="ArgumentException">Si la longueur n'est pas 32 octets.</exception>
    public static Sha256 FromBytes(ReadOnlySpan<byte> hash)
    {
        if (hash.Length != 32)
        {
            throw new ArgumentException("Un SHA-256 fait 32 octets.", nameof(hash));
        }

        return new Sha256(Convert.ToHexString(hash).ToLowerInvariant());
    }

    /// <summary>Construit à partir d'une chaîne hexadécimale de 64 caractères.</summary>
    /// <exception cref="ArgumentException">Si la chaîne n'est pas un hex de 64 caractères.</exception>
    public static Sha256 FromHex(string hex)
    {
        if (hex is null || hex.Length != 64 || !IsHex(hex))
        {
            throw new ArgumentException("Un SHA-256 est une chaîne hexadécimale de 64 caractères.", nameof(hex));
        }

        return new Sha256(hex.ToLowerInvariant());
    }

    public override string ToString() => Value;

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            var isHexDigit = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHexDigit)
            {
                return false;
            }
        }

        return true;
    }
}
