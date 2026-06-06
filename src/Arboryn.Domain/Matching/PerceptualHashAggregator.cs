using Arboryn.Domain.ValueObjects;

namespace Arboryn.Domain.Matching;

/// <summary>
/// Agrège plusieurs empreintes perceptuelles (les pHash des keyframes d'une vidéo) en une
/// seule empreinte 64 bits par vote majoritaire bit à bit : chaque bit du résultat vaut 1
/// si une stricte majorité des empreintes a ce bit à 1.
///
/// Deux encodages de la même vidéo produisent des keyframes quasi identiques, donc des
/// empreintes agrégées proches — exploitables par la même détection perceptuelle que les
/// images. Pure et déterministe.
/// </summary>
public static class PerceptualHashAggregator
{
    public static PerceptualHash MajorityVote(IReadOnlyList<PerceptualHash> hashes)
    {
        if (hashes is null || hashes.Count == 0)
        {
            throw new ArgumentException("Au moins une empreinte est requise.", nameof(hashes));
        }

        ulong result = 0;
        for (var bit = 0; bit < 64; bit++)
        {
            var ones = 0;
            for (var i = 0; i < hashes.Count; i++)
            {
                if (((hashes[i].Value >> bit) & 1UL) != 0)
                {
                    ones++;
                }
            }

            // Stricte majorité ; en cas d'égalité, le bit reste à 0 (déterministe).
            if (ones * 2 > hashes.Count)
            {
                result |= 1UL << bit;
            }
        }

        return new PerceptualHash(result);
    }
}
