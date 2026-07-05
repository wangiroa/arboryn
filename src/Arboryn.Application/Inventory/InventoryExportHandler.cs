using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Arboryn.Application.Inventory;

/// <summary>
/// Exporte l'inventaire (Inc 11, § 5.9) en CSV (matrice volume × catégorie, séparateur <c>;</c>)
/// et en JSON (instantané complet). Le contenu est produit sous forme de chaînes ; l'UI choisit
/// l'emplacement et le format d'écriture.
/// </summary>
public sealed class InventoryExportHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly InventoryDashboardHandler _dashboard;

    public InventoryExportHandler(InventoryDashboardHandler dashboard) => _dashboard = dashboard;

    public async Task<InventoryExport> BuildAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _dashboard.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        return new InventoryExport(BuildCsv(snapshot), BuildJson(snapshot));
    }

    private static string BuildCsv(InventorySnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Volume;Statut;Catégorie;Présent;EnScope;Manque;Surplus");
        foreach (var volume in snapshot.Volumes)
        {
            foreach (var cell in volume.Cells)
            {
                sb.Append(Csv(volume.Name)).Append(';')
                    .Append(Csv(volume.Status.ToString())).Append(';')
                    .Append(Csv(cell.Category.ToString())).Append(';')
                    .Append(cell.Present.ToString(CultureInfo.InvariantCulture)).Append(';')
                    .Append(cell.InScope.ToString(CultureInfo.InvariantCulture)).Append(';')
                    .Append(cell.Gap.ToString(CultureInfo.InvariantCulture)).Append(';')
                    .Append(cell.Surplus.ToString(CultureInfo.InvariantCulture))
                    .AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string BuildJson(InventorySnapshot snapshot)
    {
        var dto = new InventoryExportDto(
            new GlobalDto(snapshot.Global.LogicalFiles, snapshot.Global.FileInstances,
                snapshot.Global.TotalSpaceBytes, snapshot.Global.RedundancyRatio),
            snapshot.Categories
                .Select(c => new CategoryDto(c.Category.ToString(), c.LogicalFiles, c.SpaceBytes))
                .ToList(),
            snapshot.Volumes
                .Select(v => new VolumeDto(v.Name, v.Status.ToString(), v.HasScope, v.SpaceBytes, v.GapCount, v.SurplusCount,
                    v.Cells.Select(c => new CellDto(c.Category.ToString(), c.Present, c.InScope, c.Gap, c.Surplus)).ToList()))
                .ToList(),
            new HealthDto(snapshot.Health.OfflineVolumes, snapshot.Health.StaleVolumes,
                snapshot.Health.PendingOperations, snapshot.Health.OldestScan));
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    private static string Csv(string value)
        => value.Contains(';') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;

    private sealed record InventoryExportDto(
        GlobalDto Global, IReadOnlyList<CategoryDto> Categories, IReadOnlyList<VolumeDto> Volumes, HealthDto Health);

    private sealed record GlobalDto(long LogicalFiles, long FileInstances, long TotalSpaceBytes, double RedundancyRatio);

    private sealed record CategoryDto(string Category, int LogicalFiles, long SpaceBytes);

    private sealed record VolumeDto(
        string Name, string Status, bool HasScope, long SpaceBytes, int Gap, int Surplus, IReadOnlyList<CellDto> Cells);

    private sealed record CellDto(string Category, int Present, int InScope, int Gap, int Surplus);

    private sealed record HealthDto(int OfflineVolumes, int StaleVolumes, int PendingOperations, System.DateTime? OldestScan);
}

/// <summary>Résultat d'export : le CSV et le JSON de l'inventaire.</summary>
public sealed record InventoryExport(string Csv, string Json);
