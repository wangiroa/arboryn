using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Matching;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Détection des doublons « flous » (Inc 2) : regroupe les fichiers aux noms proches
/// (similarité ≥ seuil). Plus coûteuse que l'exacte — déclenchée à la demande.
/// Les groupes purement exacts (mêmes nom canonique + taille) sont exclus : ils
/// relèvent déjà de la détection exacte.
/// </summary>
public sealed class DetectFuzzyDuplicatesHandler
{
    private readonly IFileInstanceRepository _repository;

    public DetectFuzzyDuplicatesHandler(IFileInstanceRepository repository)
        => _repository = repository;

    public async Task<IReadOnlyList<DuplicateGroupView>> ExecuteAsync(
        VolumeId volumeId, FilePath? underRoot, double threshold, CancellationToken cancellationToken = default)
    {
        var instances = await _repository.GetActiveInstancesAsync(volumeId, underRoot, cancellationToken).ConfigureAwait(false);
        return GroupFuzzy(instances, threshold);
    }

    /// <summary>
    /// Regroupe par similarité de nom via blocage par token (on ne compare que des
    /// fichiers partageant au moins un token) + union-find. Pur et testable.
    /// </summary>
    public static IReadOnlyList<DuplicateGroupView> GroupFuzzy(
        IReadOnlyList<FileInstanceRecord> instances, double threshold)
    {
        var count = instances.Count;
        if (count < 2)
        {
            return [];
        }

        var names = new string[count];
        var tokenToIndices = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (var i = 0; i < count; i++)
        {
            names[i] = NameKey(instances[i].CanonicalName.Value);
            foreach (var token in Tokens(names[i]))
            {
                if (!tokenToIndices.TryGetValue(token, out var list))
                {
                    tokenToIndices[token] = list = new List<int>();
                }

                list.Add(i);
            }
        }

        var unionFind = new UnionFind(count);
        var comparedPairs = new HashSet<long>();

        foreach (var bucket in tokenToIndices.Values)
        {
            for (var a = 0; a < bucket.Count; a++)
            {
                for (var b = a + 1; b < bucket.Count; b++)
                {
                    int i = bucket[a], j = bucket[b];
                    var pairKey = ((long)Math.Min(i, j) << 32) | (uint)Math.Max(i, j);
                    if (!comparedPairs.Add(pairKey))
                    {
                        continue;
                    }

                    if (FuzzyName.Similarity(names[i], names[j]) >= threshold)
                    {
                        unionFind.Union(i, j);
                    }
                }
            }
        }

        return BuildGroups(instances, unionFind);
    }

    private static IReadOnlyList<DuplicateGroupView> BuildGroups(
        IReadOnlyList<FileInstanceRecord> instances, UnionFind unionFind)
    {
        var components = new Dictionary<int, List<FileInstanceRecord>>();
        for (var i = 0; i < instances.Count; i++)
        {
            var root = unionFind.Find(i);
            if (!components.TryGetValue(root, out var members))
            {
                components[root] = members = new List<FileInstanceRecord>();
            }

            members.Add(instances[i]);
        }

        var groups = new List<DuplicateGroupView>();
        foreach (var members in components.Values)
        {
            if (members.Count < 2)
            {
                continue;
            }

            // Exclure les composantes purement exactes (un seul couple nom+taille) :
            // elles sont déjà couvertes par la détection exacte.
            var distinctExact = members
                .Select(m => (m.CanonicalName.Value, m.Size))
                .Distinct()
                .Count();

            if (distinctExact > 1)
            {
                groups.Add(new DuplicateGroupView(DuplicateGroupKind.FuzzyName, members));
            }
        }

        return groups;
    }

    /// <summary>Nom canonique sans extension (la détection floue porte sur le nom, pas le type).</summary>
    private static string NameKey(string canonicalName)
    {
        var dot = canonicalName.LastIndexOf('.');
        return dot > 0 ? canonicalName[..dot] : canonicalName;
    }

    private static IEnumerable<string> Tokens(string name) =>
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct();
}
