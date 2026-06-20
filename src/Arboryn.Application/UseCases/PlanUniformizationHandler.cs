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

        var taxonomyCache = new Dictionary<MediaCategory, CategoryTaxonomy?>();
        var prepared = new List<PreparedInstance>();
        var skipped = 0;

        // Passe 1 : résout catégorie/taxonomie et métadonnées de chaque fichier.
        foreach (var instance in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Préfère la catégorie affinée du LogicalFile (ISBN→Book Inc 4, triage→OfficialDocument
            // Inc 7) ; à défaut (non rattaché ou encore « unknown »), retombe sur l'extension.
            var category = instance.Category is { } refined && refined != MediaCategory.Unknown
                ? refined
                : MediaClassifier.FromExtension(instance.Path.Extension);
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

            var fileStem = Path.GetFileNameWithoutExtension(instance.Path.Value);
            var directory = Path.GetDirectoryName(instance.Path.Value) ?? string.Empty;
            var isPart = MultiFileWork.IsPartFile(category, fileStem);

            prepared.Add(new PreparedInstance(instance, category, taxonomy, values, fileStem, directory, isPart));
        }

        // Numérotation au niveau de l'œuvre : les fichiers « parties » d'un même dossier sont
        // numérotés à largeur constante (« 001 » … « 010 »), à partir du premier.
        var chapterNumbers = BuildChapterNumbers(prepared);

        // Passe 2 : calcule l'emplacement canonique de chaque fichier.
        var targets = new List<(FileInstanceRecord Instance, string TargetRelative)>();
        foreach (var item in prepared)
        {
            chapterNumbers.TryGetValue(item.Instance.Id, out var chapterNumber);
            var directoryName = Path.GetFileName(item.Directory);
            var fields = TemplateFields.From(
                item.Category, item.Values, item.Instance.Path.Extension,
                item.FileStem, directoryName, chapterNumber);

            var placement = _resolver.Resolve(item.Taxonomy, fields);
            if (placement is null)
            {
                skipped++;
                continue;
            }

            targets.Add((item.Instance, placement.RelativePath));
        }

        var (operations, alreadyCanonical) = BuildOperations(targets, libraryRoot.Value, _mover.Exists);

        // Cibles « à déplacer » (hors fichiers déjà conformes), conservées pour permettre un
        // recalcul du plan quand l'utilisateur (dé)sélectionne des opérations dans l'aperçu :
        // les suffixes de désambiguïsation « (2) » ne doivent refléter que les opérations retenues.
        var moveTargets = targets
            .Where(t => !PathEquals(t.Instance.Path.Value, Path.Combine(libraryRoot.Value, t.TargetRelative)))
            .Select(t => new PlannedTarget(t.Instance, t.TargetRelative))
            .ToList();

        return new UniformizationPlan(operations, alreadyCanonical, skipped, moveTargets);
    }

    /// <summary>
    /// Recalcule les opérations pour un sous-ensemble de cibles sélectionnées (aperçu interactif) :
    /// rejoue la résolution de collisions sur ces seules cibles, en évitant les fichiers présents
    /// sur disque (dont les fichiers déjà conformes et les opérations décochées restées en place).
    /// </summary>
    public IReadOnlyList<PlannedOperation> RebuildOperations(
        IReadOnlyList<PlannedTarget> selectedTargets, FilePath libraryRoot)
    {
        var tuples = selectedTargets
            .Select(t => (t.Instance, t.IdealRelative))
            .ToList();
        return BuildOperations(tuples, libraryRoot.Value, _mover.Exists).Operations;
    }

    /// <summary>
    /// Numérote les fichiers « parties » d'œuvres multi-fichiers, regroupés par dossier :
    /// chaque groupe reçoit des numéros zero-paddés à largeur constante (cf.
    /// <see cref="MultiFileWork.NumberParts"/>). Renvoie une table FileInstanceId → numéro.
    /// </summary>
    private static Dictionary<FileInstanceId, string> BuildChapterNumbers(
        IReadOnlyList<PreparedInstance> prepared)
    {
        var numbers = new Dictionary<FileInstanceId, string>();
        var groups = prepared
            .Where(p => p.IsPart)
            .GroupBy(p => p.Directory, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var members = group.ToList();
            var files = members
                .Select(m =>
                {
                    m.Values.TryGetValue(MetadataKeys.TrackNumber, out var track);
                    return (m.FileStem, TrackTag: track);
                })
                .ToList();

            var labels = MultiFileWork.NumberParts(files);
            for (var i = 0; i < members.Count; i++)
            {
                numbers[members[i].Instance.Id] = labels[i];
            }
        }

        return numbers;
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

    /// <summary>Données d'un fichier résolues en passe 1, réutilisées pour la numérotation et le placement.</summary>
    private sealed record PreparedInstance(
        FileInstanceRecord Instance,
        MediaCategory Category,
        CategoryTaxonomy Taxonomy,
        Dictionary<string, string> Values,
        string FileStem,
        string Directory,
        bool IsPart);
}

/// <summary>Une opération d'uniformisation planifiée (non exécutée).</summary>
public sealed record PlannedOperation(FileInstanceId Id, FilePath OldPath, FilePath NewPath, OperationKind Kind);

/// <summary>
/// Cible canonique « à déplacer » d'un fichier (avant résolution de collision) : conservée pour
/// recalculer le plan sur un sous-ensemble sélectionné. <see cref="IdealRelative"/> est le chemin
/// cible relatif à la racine de bibliothèque, hors suffixe de désambiguïsation.
/// </summary>
public sealed record PlannedTarget(FileInstanceRecord Instance, string IdealRelative);

/// <summary>
/// Plan d'uniformisation : opérations à exécuter, fichiers déjà canoniques (aucun changement),
/// fichiers ignorés (catégorie non uniformisable ou champ requis manquant), et les cibles
/// « à déplacer » (<see cref="Targets"/>) permettant un recalcul à la (dé)sélection.
/// </summary>
public sealed record UniformizationPlan(
    IReadOnlyList<PlannedOperation> Operations,
    int AlreadyCanonical,
    int Skipped,
    IReadOnlyList<PlannedTarget>? Targets = null);
