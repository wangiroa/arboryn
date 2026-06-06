using System.IO;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Metadata;
using Arboryn.Domain.Taxonomy;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Calcule le plan d'uniformisation intra-volume (Inc 6) : pour chaque FileInstance sous la
/// racine de bibliothèque, déduit l'emplacement canonique cible (taxonomie + métadonnées),
/// le compare au chemin actuel, et produit les opérations rename/move nécessaires en
/// résolvant les collisions par suffixe « (2) », « (3) »… Ne touche pas au disque.
/// </summary>
public sealed class PlanUniformizationHandler
{
    private readonly IFileInstanceRepository _instances;
    private readonly IFileMetadataRepository _metadata;
    private readonly ITaxonomyRepository _taxonomies;
    private readonly CanonicalPathResolver _resolver;
    private readonly IFileMover _mover;

    public PlanUniformizationHandler(
        IFileInstanceRepository instances,
        IFileMetadataRepository metadata,
        ITaxonomyRepository taxonomies,
        CanonicalPathResolver resolver,
        IFileMover mover)
    {
        _instances = instances;
        _metadata = metadata;
        _taxonomies = taxonomies;
        _resolver = resolver;
        _mover = mover;
    }

    public async Task<UniformizationPlan> ExecuteAsync(
        VolumeId volumeId, FilePath libraryRoot, CancellationToken cancellationToken = default)
    {
        var instances = await _instances
            .GetActiveInstancesAsync(volumeId, libraryRoot, cancellationToken).ConfigureAwait(false);

        var targets = new List<(FileInstanceRecord Instance, string TargetRelative)>();
        var taxonomyCache = new Dictionary<MediaCategory, CategoryTaxonomy?>();
        var skipped = 0;

        foreach (var instance in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var category = MediaClassifier.FromExtension(instance.Path.Extension);
            if (!taxonomyCache.TryGetValue(category, out var taxonomy))
            {
                taxonomy = await _taxonomies.GetAsync(category, cancellationToken).ConfigureAwait(false);
                taxonomyCache[category] = taxonomy;
            }

            if (taxonomy is null)
            {
                skipped++;
                continue;
            }

            var fused = await _metadata.GetFusedAsync(instance.Id, cancellationToken).ConfigureAwait(false);
            var values = fused
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value.Value!, StringComparer.Ordinal);

            var fields = TemplateFields.From(category, values, instance.Path.Extension);
            var placement = _resolver.Resolve(taxonomy, fields);
            if (placement is null)
            {
                skipped++;
                continue;
            }

            targets.Add((instance, placement.RelativePath));
        }

        var (operations, alreadyCanonical) = BuildOperations(targets, libraryRoot.Value, _mover.Exists);
        return new UniformizationPlan(operations, alreadyCanonical, skipped);
    }

    /// <summary>
    /// Construit les opérations à partir des cibles relatives, en résolvant les collisions.
    /// Pure et testable : <paramref name="exists"/> sonde l'existence sur disque, et les
    /// cibles déjà réservées dans le lot sont suivies en mémoire.
    /// </summary>
    public static (IReadOnlyList<PlannedOperation> Operations, int AlreadyCanonical) BuildOperations(
        IReadOnlyList<(FileInstanceRecord Instance, string TargetRelative)> targets,
        string libraryRoot,
        Func<FilePath, bool> exists)
    {
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var operations = new List<PlannedOperation>();
        var alreadyCanonical = 0;

        foreach (var (instance, targetRelative) in targets)
        {
            var source = instance.Path.Value;
            var ideal = Path.Combine(libraryRoot, targetRelative);

            if (PathEquals(source, ideal))
            {
                claimed.Add(ideal);
                alreadyCanonical++;
                continue;
            }

            var final = ideal;
            var suffix = 2;
            while (claimed.Contains(final) || (exists(FilePath.From(final)) && !PathEquals(final, source)))
            {
                final = AddSuffix(ideal, suffix++);
            }

            claimed.Add(final);

            var kind = PathEquals(Path.GetDirectoryName(source) ?? string.Empty, Path.GetDirectoryName(final) ?? string.Empty)
                ? OperationKind.Rename
                : OperationKind.Move;

            operations.Add(new PlannedOperation(instance.Id, FilePath.From(source), FilePath.From(final), kind));
        }

        return (operations, alreadyCanonical);
    }

    private static bool PathEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string AddSuffix(string path, int suffix)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return Path.Combine(directory, $"{stem} ({suffix}){extension}");
    }
}

/// <summary>Une opération d'uniformisation planifiée (non exécutée).</summary>
public sealed record PlannedOperation(FileInstanceId Id, FilePath OldPath, FilePath NewPath, OperationKind Kind);

/// <summary>
/// Plan d'uniformisation : opérations à exécuter, fichiers déjà canoniques (aucun changement),
/// et fichiers ignorés (catégorie non uniformisable ou champ requis manquant).
/// </summary>
public sealed record UniformizationPlan(
    IReadOnlyList<PlannedOperation> Operations, int AlreadyCanonical, int Skipped);
