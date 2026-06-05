using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Matching;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Détection des doublons perceptuels (Inc 5) : regroupe les images dont les empreintes
/// perceptuelles sont à distance de Hamming ≤ seuil — donc les vraies copies recompressées
/// ou redimensionnées. S'appuie sur un arbre BK pour la recherche par rayon, puis union-find
/// pour agréger les composantes connexes.
/// </summary>
public sealed class DetectPerceptualDuplicatesHandler
{
    /// <summary>Seuil par défaut (sur 64 bits) ≈ 84 % de similarité.</summary>
    public const int DefaultMaxDistance = 10;

    private readonly IPerceptualHashStore _store;

    public DetectPerceptualDuplicatesHandler(IPerceptualHashStore store)
        => _store = store;

    public async Task<IReadOnlyList<DuplicateGroupView>> ExecuteAsync(
        VolumeId volumeId,
        FilePath? underRoot = null,
        int maxDistance = DefaultMaxDistance,
        CancellationToken cancellationToken = default)
    {
        var hashed = await _store.GetHashedAsync(volumeId, underRoot, cancellationToken).ConfigureAwait(false);
        return Group(hashed, maxDistance);
    }

    /// <summary>
    /// Regroupe les instances par proximité d'empreinte et projette en vues UI.
    /// Ne conserve que les groupes de ≥ 2 membres.
    /// </summary>
    public static IReadOnlyList<DuplicateGroupView> Group(
        IReadOnlyList<PerceptualHashedInstance> items, int maxDistance)
        => GroupCore(items, maxDistance)
            .Select(g => new DuplicateGroupView(
                DuplicateGroupKind.Perceptual, g.Members.Select(m => m.Instance).ToList()))
            .ToList();

    /// <summary>
    /// Cœur du regroupement perceptuel (arbre BK + union-find). Méthode pure, testable.
    /// Chaque groupe porte un représentant stable (empreinte minimale) servant de
    /// signature au LogicalFile lors de la promotion.
    /// </summary>
    public static IReadOnlyList<PerceptualGroup> GroupCore(
        IReadOnlyList<PerceptualHashedInstance> items, int maxDistance)
    {
        var count = items.Count;
        if (count < 2)
        {
            return [];
        }

        var tree = new PerceptualHashBkTree();
        for (var i = 0; i < count; i++)
        {
            tree.Add(items[i].Hash, i);
        }

        var unionFind = new UnionFind(count);
        for (var i = 0; i < count; i++)
        {
            foreach (var j in tree.Search(items[i].Hash, maxDistance))
            {
                if (j != i)
                {
                    unionFind.Union(i, j);
                }
            }
        }

        var components = new Dictionary<int, List<PerceptualHashedInstance>>();
        for (var i = 0; i < count; i++)
        {
            var root = unionFind.Find(i);
            if (!components.TryGetValue(root, out var members))
            {
                components[root] = members = new List<PerceptualHashedInstance>();
            }

            members.Add(items[i]);
        }

        return components.Values
            .Where(members => members.Count > 1)
            .Select(members => new PerceptualGroup(
                new PerceptualHash(members.Min(m => m.Hash.Value)), members))
            .ToList();
    }
}

/// <summary>
/// Groupe d'images perceptuellement proches, avec son représentant (empreinte minimale,
/// donc stable) utilisé comme signature de LogicalFile.
/// </summary>
public sealed record PerceptualGroup(
    PerceptualHash Representative,
    IReadOnlyList<PerceptualHashedInstance> Members);
