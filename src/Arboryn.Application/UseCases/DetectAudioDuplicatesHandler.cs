using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Matching;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Détection des doublons acoustiques (Inc 5) : regroupe les fichiers audio dont les
/// empreintes Chromaprint sont très proches (même morceau, ré-encodé). Comparaison par
/// paires, pré-filtrée par durée proche (longueur d'empreinte), puis union-find.
/// </summary>
public sealed class DetectAudioDuplicatesHandler
{
    /// <summary>Les empreintes dont les durées diffèrent de plus de ~20 % ne sont pas comparées.</summary>
    private const double LengthRatioFloor = 0.8;

    private readonly IAudioFingerprintStore _store;

    public DetectAudioDuplicatesHandler(IAudioFingerprintStore store)
        => _store = store;

    public async Task<IReadOnlyList<DuplicateGroupView>> ExecuteAsync(
        VolumeId volumeId,
        FilePath? underRoot = null,
        double minSimilarity = ChromaprintMatcher.DefaultMinSimilarity,
        CancellationToken cancellationToken = default)
    {
        var fingerprinted = await _store.GetFingerprintedAsync(volumeId, underRoot, cancellationToken).ConfigureAwait(false);
        return GroupCore(fingerprinted, minSimilarity)
            .Select(g => new DuplicateGroupView(
                DuplicateGroupKind.Perceptual, g.Members.Select(m => m.Instance).ToList()))
            .ToList();
    }

    /// <summary>
    /// Cœur du regroupement acoustique. Méthode pure, testable. Chaque groupe porte un
    /// représentant stable (empreinte de digest minimal) servant de signature de LogicalFile.
    /// </summary>
    public static IReadOnlyList<AudioGroup> GroupCore(
        IReadOnlyList<AudioFingerprintedInstance> items, double minSimilarity)
    {
        var count = items.Count;
        if (count < 2)
        {
            return [];
        }

        var unionFind = new UnionFind(count);
        for (var i = 0; i < count; i++)
        {
            for (var j = i + 1; j < count; j++)
            {
                if (!DurationsComparable(items[i].Fingerprint, items[j].Fingerprint))
                {
                    continue;
                }

                if (ChromaprintMatcher.Similarity(items[i].Fingerprint, items[j].Fingerprint) >= minSimilarity)
                {
                    unionFind.Union(i, j);
                }
            }
        }

        var components = new Dictionary<int, List<AudioFingerprintedInstance>>();
        for (var i = 0; i < count; i++)
        {
            var root = unionFind.Find(i);
            if (!components.TryGetValue(root, out var members))
            {
                components[root] = members = new List<AudioFingerprintedInstance>();
            }

            members.Add(items[i]);
        }

        return components.Values
            .Where(members => members.Count > 1)
            .Select(members => new AudioGroup(Representative(members), members))
            .ToList();
    }

    private static bool DurationsComparable(AudioFingerprint a, AudioFingerprint b)
    {
        var shorter = Math.Min(a.Length, b.Length);
        var longer = Math.Max(a.Length, b.Length);
        return longer == 0 || shorter >= LengthRatioFloor * longer;
    }

    private static AudioFingerprint Representative(IReadOnlyList<AudioFingerprintedInstance> members)
        => members
            .OrderBy(m => m.Fingerprint.StableDigest(), StringComparer.Ordinal)
            .First()
            .Fingerprint;
}

/// <summary>
/// Groupe d'enregistrements acoustiquement identiques, avec son représentant (empreinte
/// de digest minimal, donc stable) utilisé comme signature de LogicalFile.
/// </summary>
public sealed record AudioGroup(
    AudioFingerprint Representative,
    IReadOnlyList<AudioFingerprintedInstance> Members);
