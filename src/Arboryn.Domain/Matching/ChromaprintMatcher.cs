using System.Numerics;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Domain.Matching;

/// <summary>
/// Comparaison de deux empreintes acoustiques Chromaprint. La similarité est
/// 1 − BER (bit error rate) sur le meilleur alignement trame à trame, en balayant
/// un petit décalage pour absorber les différences de silence en tête (encodeurs).
/// Pure et déterministe.
/// </summary>
public static class ChromaprintMatcher
{
    /// <summary>Seuil par défaut : ≥ 0,90 ⇔ BER ≤ 10 %, robuste au ré-encodage, sans faux positifs.</summary>
    public const double DefaultMinSimilarity = 0.90;

    /// <summary>Décalage maximal exploré (en trames) ≈ 6 s.</summary>
    public const int DefaultMaxOffset = 50;

    /// <summary>Recouvrement minimal exigé (en trames) pour qu'une comparaison soit significative.</summary>
    public const int DefaultMinOverlap = 10;

    /// <summary>
    /// Renvoie la meilleure similarité (0..1) entre deux empreintes, sur tous les
    /// décalages dans [−maxOffset, +maxOffset] ayant un recouvrement suffisant.
    /// 0 si aucun alignement valable.
    /// </summary>
    public static double Similarity(
        AudioFingerprint a,
        AudioFingerprint b,
        int maxOffset = DefaultMaxOffset,
        int minOverlap = DefaultMinOverlap)
    {
        var x = a.SubFingerprints;
        var y = b.SubFingerprints;
        if (x.Count == 0 || y.Count == 0)
        {
            return 0.0;
        }

        var best = 0.0;
        for (var offset = -maxOffset; offset <= maxOffset; offset++)
        {
            // Aligne x[i] avec y[i + offset].
            var start = Math.Max(0, -offset);
            var end = Math.Min(x.Count, y.Count - offset);
            var overlap = end - start;
            if (overlap < minOverlap)
            {
                continue;
            }

            long differingBits = 0;
            for (var i = start; i < end; i++)
            {
                differingBits += BitOperations.PopCount(x[i] ^ y[i + offset]);
            }

            var ber = differingBits / (32.0 * overlap);
            var similarity = 1.0 - ber;
            if (similarity > best)
            {
                best = similarity;
            }
        }

        return best;
    }
}
