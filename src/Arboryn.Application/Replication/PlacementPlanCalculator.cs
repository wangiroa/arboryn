using System;
using System.Collections.Generic;
using System.Linq;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Replication;

/// <summary>
/// Cœur de la réplication (Inc 10, § 5.5, étapes 2 à 4) : à partir du catalogue logique et des
/// périmètres de chaque volume, calcule le diff réel/cible et en déduit les opérations
/// (rename / move intra-volume, copy inter-volume, delete de surplus et de doublons), l'impact
/// espace par volume, et les conflits de versions. Fonction pure et déterministe — aucune I/O.
/// </summary>
/// <remarks>
/// Politique de sûreté : un <c>LogicalFile</c> dont les instances divergent en taille (versions
/// différentes) ne produit AUCUNE opération et remonte comme conflit — l'utilisateur tranche.
/// Les collisions de chemin canonique entre œuvres distinctes ne sont pas gérées ici (la
/// taxonomie est supposée produire des chemins uniques par œuvre).
/// </remarks>
public sealed class PlacementPlanCalculator
{
    public PlacementPlan Calculate(
        IReadOnlyList<ReplicationCatalogEntry> catalog,
        IReadOnlyList<VolumeScope> volumes)
    {
        var operations = new List<PlacementOperation>();
        var conflicts = new List<PlacementConflict>();
        var spaceDelta = volumes.ToDictionary(v => v.VolumeId, _ => 0L);
        var skipped = 0;

        var volumeById = volumes.ToDictionary(v => v.VolumeId);

        foreach (var entry in catalog)
        {
            if (entry.Instances.Count == 0)
            {
                continue; // œuvre sans instance active : rien à placer
            }

            // --- Détection de conflit : tailles divergentes = versions différentes ---
            if (entry.Instances.Select(i => i.Size).Distinct().Count() > 1)
            {
                conflicts.Add(new PlacementConflict(
                    entry.LogicalFileId,
                    "Versions divergentes du même fichier (tailles distinctes) : "
                        + string.Join(", ", entry.Instances.Select(i => i.Size).Distinct().OrderBy(s => s)) + " octets",
                    entry.Instances.Select(i => i.VolumeId).Distinct().ToList()));
                continue;
            }

            var byVolume = entry.Instances
                .GroupBy(i => i.VolumeId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<ReplicaInstance>)g.ToList());

            // Source de copie : de préférence une instance sur un volume en ligne.
            var source = PickSource(entry.Instances, volumeById);
            var unplaceableInScope = false;

            foreach (var volume in volumes)
            {
                var inScope = volume.Scope.Matches(entry.Subject);
                var present = byVolume.TryGetValue(volume.VolumeId, out var list)
                    ? list
                    : System.Array.Empty<ReplicaInstance>();

                if (!inScope)
                {
                    // Hors scope : tout ce qui est présent est en surplus → suppression.
                    foreach (var surplus in present)
                    {
                        operations.Add(Delete(entry, volume.VolumeId, surplus));
                        spaceDelta[volume.VolumeId] -= surplus.Size;
                    }

                    continue;
                }

                // En scope mais non plaçable (pas de chemin canonique) : on laisse les instances
                // présentes en place et on ne copie pas ; on note l'œuvre comme ignorée.
                if (entry.CanonicalRelativePath is null)
                {
                    unplaceableInScope = true;
                    continue;
                }

                var canonical = entry.CanonicalRelativePath;

                if (present.Count == 0)
                {
                    // Manquant sur un volume en scope → copie inter-volume. On mémorise l'instance
                    // source (id + chemin actuel) pour permettre l'exécution et l'annulation.
                    if (source is { } src)
                    {
                        operations.Add(new PlacementOperation(
                            OperationKind.Copy,
                            entry.LogicalFileId,
                            InstanceId: src.Id,
                            SourceVolumeId: src.VolumeId,
                            TargetVolumeId: volume.VolumeId,
                            OldRelativePath: src.RelativePath,
                            NewRelativePath: canonical,
                            Size: entry.Size));
                        spaceDelta[volume.VolumeId] += entry.Size;
                    }

                    continue;
                }

                // Présent : garder un exemplaire au chemin canonique, supprimer les doublons.
                var keeper = present.FirstOrDefault(p => PathEquals(p.RelativePath, canonical))
                    ?? present.OrderBy(p => p.RelativePath, StringComparer.OrdinalIgnoreCase).First();

                if (!PathEquals(keeper.RelativePath, canonical))
                {
                    var kind = SameDirectory(keeper.RelativePath, canonical)
                        ? OperationKind.Rename
                        : OperationKind.Move;
                    operations.Add(new PlacementOperation(
                        kind,
                        entry.LogicalFileId,
                        keeper.Id,
                        volume.VolumeId,
                        volume.VolumeId,
                        keeper.RelativePath,
                        canonical,
                        keeper.Size));
                    // Intra-volume : pas d'impact espace.
                }

                foreach (var duplicate in present.Where(p => p.Id != keeper.Id))
                {
                    operations.Add(Delete(entry, volume.VolumeId, duplicate));
                    spaceDelta[volume.VolumeId] -= duplicate.Size;
                }
            }

            if (unplaceableInScope)
            {
                skipped++;
            }
        }

        return new PlacementPlan(operations, conflicts, spaceDelta, skipped);
    }

    private static PlacementOperation Delete(ReplicationCatalogEntry entry, VolumeId volumeId, ReplicaInstance instance)
        => new(
            OperationKind.Delete,
            entry.LogicalFileId,
            instance.Id,
            volumeId,
            volumeId,
            instance.RelativePath,
            instance.RelativePath,
            instance.Size);

    /// <summary>Choisit une instance source pour une copie : priorité aux volumes en ligne.</summary>
    private static ReplicaInstance? PickSource(
        IReadOnlyList<ReplicaInstance> instances,
        IReadOnlyDictionary<VolumeId, VolumeScope> volumeById)
    {
        var online = instances.FirstOrDefault(i =>
            volumeById.TryGetValue(i.VolumeId, out var v) && v.Status == VolumeStatus.Online);
        return online ?? instances.FirstOrDefault();
    }

    private static bool PathEquals(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static bool SameDirectory(string a, string b)
        => string.Equals(DirectoryOf(a), DirectoryOf(b), StringComparison.OrdinalIgnoreCase);

    private static string DirectoryOf(string relativePath)
    {
        var normalized = Normalize(relativePath);
        var idx = normalized.LastIndexOf('\\');
        return idx < 0 ? string.Empty : normalized[..idx];
    }

    private static string Normalize(string path)
        => path.Replace('/', '\\').Trim('\\');
}
