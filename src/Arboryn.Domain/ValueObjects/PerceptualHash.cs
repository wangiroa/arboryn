using System.Globalization;
using System.Numerics;

namespace Arboryn.Domain.ValueObjects;

/// <summary>
/// Empreinte perceptuelle 64 bits d'un média (image pour l'instant). Deux fichiers au
/// contenu visuellement proche ont une faible distance de Hamming entre leurs empreintes,
/// même après recompression ou redimensionnement. Sérialisée en 16 caractères hexadécimaux.
/// </summary>
public readonly record struct PerceptualHash(ulong Value)
{
    /// <summary>Forme hexadécimale 16 caractères, stockée en base (colonne <c>phash</c>).</summary>
    public string ToHex() => Value.ToString("x16", CultureInfo.InvariantCulture);

    public static PerceptualHash FromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            throw new ArgumentException("L'empreinte hexadécimale ne peut pas être vide.", nameof(hex));
        }

        return new PerceptualHash(ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    /// <summary>Distance de Hamming (0..64) : nombre de bits qui diffèrent entre deux empreintes.</summary>
    public int HammingDistance(PerceptualHash other) => BitOperations.PopCount(Value ^ other.Value);

    public override string ToString() => ToHex();
}
