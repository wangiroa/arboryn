using System.IO;
using System.Text.RegularExpressions;
using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Metadata;
using Arboryn.Domain.Replication;
using Arboryn.Domain.Taxonomy;

namespace Arboryn.Application.Replication;

/// <summary>
/// Assembleur du read-model de réplication (Inc 10, § 5.5 étapes 1-2) : transforme le catalogue
/// brut (œuvres + instances) en <see cref="ReplicationCatalogEntry"/> exploitables par le
/// calculateur, en enrichissant chaque œuvre depuis une instance représentative — chemin
/// canonique (taxonomie + métadonnées, comme l'uniformisation), sous-catégorie et année (pour
/// l'évaluation de scope).
/// </summary>
/// <remarks>
/// Le chemin canonique est recalculé par instance représentative. Pour les œuvres multi-fichiers
/// (livres audio / BD découpés), la numérotation à largeur constante — qui dépend du groupe de
/// pistes — n'est pas répliquée ici : le numéro dérive de la piste de l'instance seule. La
/// convergence exacte reste assurée après passage de l'uniformisation intra-volume (Inc 6).
/// </remarks>
public sealed class BuildReplicationCatalogHandler
{
    private const string SubcategoryKey = "subcategory";
    private static readonly Regex YearRegex = new(@"(19|20)\d{2}", RegexOptions.Compiled);

    private readonly IReplicationCatalogReader _reader;
    private readonly IFileMetadataRepository _metadata;
    private readonly ITaxonomyRepository _taxonomies;
    private readonly CanonicalPathResolver _resolver;

    public BuildReplicationCatalogHandler(
        IReplicationCatalogReader reader,
        IFileMetadataRepository metadata,
        ITaxonomyRepository taxonomies,
        CanonicalPathResolver resolver)
    {
        _reader = reader;
        _metadata = metadata;
        _taxonomies = taxonomies;
        _resolver = resolver;
    }

    /// <summary>
    /// Assemble le catalogue restreint aux volumes présents dans <paramref name="volumeRoots"/>
    /// (les instances hors de cet ensemble — p.ex. le volume « default » — sont ignorées). Les
    /// chemins stockés sont absolus ; ils sont rendus relatifs à la racine enrôlée de leur volume
    /// (<paramref name="volumeRoots"/>) pour être comparables au chemin canonique. Les œuvres
    /// n'ayant plus d'instance dans le périmètre sont écartées.
    /// </summary>
    public async Task<IReadOnlyList<ReplicationCatalogEntry>> BuildAsync(
        IReadOnlyDictionary<Domain.ValueObjects.VolumeId, string?> volumeRoots,
        CancellationToken cancellationToken = default)
    {
        var rawCatalog = await _reader.GetAsync(cancellationToken).ConfigureAwait(false);
        var taxonomyCache = new Dictionary<MediaCategory, CategoryTaxonomy?>();
        var entries = new List<ReplicationCatalogEntry>();

        foreach (var logical in rawCatalog)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var instances = logical.Instances
                .Where(i => volumeRoots.ContainsKey(i.VolumeId))
                .Select(i => i with { RelativePath = Relativize(i.RelativePath, volumeRoots[i.VolumeId]) })
                .ToList();
            if (instances.Count == 0)
            {
                continue;
            }

            // Instance représentative déterministe (les instances partagent le même contenu).
            var representative = instances
                .OrderBy(i => i.VolumeId.Value, StringComparer.Ordinal)
                .ThenBy(i => i.RelativePath, StringComparer.OrdinalIgnoreCase)
                .First();

            var extension = Path.GetExtension(representative.RelativePath);
            var category = logical.Category != MediaCategory.Unknown
                ? logical.Category
                : MediaClassifier.FromExtension(extension);

            if (!taxonomyCache.TryGetValue(category, out var taxonomy))
            {
                taxonomy = await _taxonomies.GetAsync(category, cancellationToken).ConfigureAwait(false);
                taxonomyCache[category] = taxonomy;
            }

            var fused = await _metadata.GetFusedAsync(representative.Id, cancellationToken).ConfigureAwait(false);
            var values = fused
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value.Value!, StringComparer.Ordinal);

            var subcategory = values.TryGetValue(SubcategoryKey, out var sub) ? sub : null;
            var year = ExtractYear(values);
            var canonical = taxonomy is null ? null : ResolveCanonical(category, taxonomy, values, representative.RelativePath, extension);

            var subject = new ScopeSubject(category, subcategory, year);
            var replicaInstances = instances
                .Select(i => new ReplicaInstance(i.Id, i.VolumeId, i.RelativePath, i.Size))
                .ToList();

            entries.Add(new ReplicationCatalogEntry(
                logical.Id, subject, canonical, representative.Size, replicaInstances));
        }

        return entries;
    }

    private string? ResolveCanonical(
        MediaCategory category,
        CategoryTaxonomy taxonomy,
        IReadOnlyDictionary<string, string> values,
        string relativePath,
        string extension)
    {
        var fileStem = Path.GetFileNameWithoutExtension(relativePath);
        var directory = Path.GetDirectoryName(relativePath) ?? string.Empty;
        var parentDirectoryName = Path.GetFileName(directory);

        string? chapterNumber = null;
        if (MultiFileWork.IsPartFile(category, fileStem))
        {
            values.TryGetValue(MetadataKeys.TrackNumber, out var track);
            chapterNumber = MultiFileWork.NumberParts(new[] { (fileStem, (string?)track) })[0];
        }

        var fields = TemplateFields.From(category, values, extension, fileStem, parentDirectoryName, chapterNumber);
        return _resolver.Resolve(taxonomy, fields)?.RelativePath;
    }

    /// <summary>
    /// Rend un chemin absolu relatif à la racine enrôlée de son volume. Repli sur le chemin
    /// d'origine si la racine est inconnue ou si le chemin n'en descend pas (cas dégradé).
    /// </summary>
    private static string Relativize(string absolutePath, string? root)
    {
        if (string.IsNullOrEmpty(root))
        {
            return absolutePath;
        }

        var trimmed = root.TrimEnd('\\', '/');
        if (string.Equals(absolutePath, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (absolutePath.Length > trimmed.Length
            && absolutePath.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)
            && (absolutePath[trimmed.Length] == '\\' || absolutePath[trimmed.Length] == '/'))
        {
            return absolutePath[(trimmed.Length + 1)..];
        }

        return absolutePath;
    }

    /// <summary>Année de scope : champ <c>year</c> si présent, sinon une année 19xx/20xx dans <c>date</c>/<c>date_taken</c>.</summary>
    private static int? ExtractYear(IReadOnlyDictionary<string, string> values)
    {
        if (values.TryGetValue(MetadataKeys.Year, out var yearRaw))
        {
            var match = YearRegex.Match(yearRaw);
            if (match.Success && int.TryParse(match.Value, out var y))
            {
                return y;
            }
        }

        foreach (var key in new[] { MetadataKeys.Date, MetadataKeys.DateTaken })
        {
            if (values.TryGetValue(key, out var raw))
            {
                var match = YearRegex.Match(raw);
                if (match.Success && int.TryParse(match.Value, out var y))
                {
                    return y;
                }
            }
        }

        return null;
    }
}
