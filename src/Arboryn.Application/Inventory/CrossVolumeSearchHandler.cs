using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Inventory;

/// <summary>
/// Recherche cross-volume « où est X ? » (Inc 11, § 5.9) : regroupe les instances trouvées par
/// œuvre et indique sur quels volumes chacune est présente. Conçu pour être rapide (requête SQL
/// indexée bornée), utilisable en frappe depuis la barre de recherche.
/// </summary>
public sealed class CrossVolumeSearchHandler
{
    private const int MaxHits = 400;

    private readonly IInventoryReader _reader;

    public CrossVolumeSearchHandler(IInventoryReader reader) => _reader = reader;

    public async Task<IReadOnlyList<CrossVolumeSearchResult>> SearchAsync(
        string query, int maxResults = 50, CancellationToken cancellationToken = default)
    {
        var hits = await _reader.SearchAsync(query, MaxHits, cancellationToken).ConfigureAwait(false);

        var results = new List<CrossVolumeSearchResult>();
        var byLogical = new Dictionary<string, CrossVolumeSearchResult>(StringComparer.Ordinal);
        foreach (var hit in hits)
        {
            if (!byLogical.TryGetValue(hit.LogicalFileId.Value, out var result))
            {
                result = new CrossVolumeSearchResult(
                    hit.LogicalFileId, hit.Category, Path.GetFileName(hit.RelativePath), new List<string>());
                byLogical[hit.LogicalFileId.Value] = result;
                results.Add(result);
                if (results.Count >= maxResults)
                {
                    break;
                }
            }

            if (!result.VolumeNames.Contains(hit.VolumeName))
            {
                ((List<string>)result.VolumeNames).Add(hit.VolumeName);
            }
        }

        return results;
    }
}

/// <summary>Résultat de recherche : une œuvre et les volumes où elle est présente.</summary>
public sealed record CrossVolumeSearchResult(
    LogicalFileId LogicalFileId, MediaCategory Category, string FileName, IReadOnlyList<string> VolumeNames);
