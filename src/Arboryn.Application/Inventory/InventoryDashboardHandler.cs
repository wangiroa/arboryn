using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Replication;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Inventory;

/// <summary>
/// Construit l'instantané du tableau de bord inventaire (Inc 11, § 5.9) : matrice volume ×
/// catégorie (présent / en-scope / manque / surplus), synthèse par catégorie, compteurs globaux
/// et indicateurs de santé. L'appartenance au périmètre est évaluée à la granularité catégorie
/// (les périmètres produits par l'UI sont Tout / catégories / rien), sans lecture des métadonnées.
/// </summary>
public sealed class InventoryDashboardHandler
{
    /// <summary>Un volume dont le dernier scan remonte à plus de cette durée est signalé « scan ancien ».</summary>
    public static readonly TimeSpan StaleScanAfter = TimeSpan.FromDays(30);

    private static readonly IReadOnlyList<MediaCategory> Categories = Enum.GetValues<MediaCategory>()
        .Where(c => c != MediaCategory.Unknown)
        .ToList();

    private readonly IInventoryReader _reader;
    private readonly IVolumeRepository _volumes;
    private readonly IReplicationScopeRepository _scopes;
    private readonly IOperationJournal _journal;

    public InventoryDashboardHandler(
        IInventoryReader reader,
        IVolumeRepository volumes,
        IReplicationScopeRepository scopes,
        IOperationJournal journal)
    {
        _reader = reader;
        _volumes = volumes;
        _scopes = scopes;
        _journal = journal;
    }

    public async Task<InventorySnapshot> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var presence = await _reader.GetPresenceAsync(cancellationToken).ConfigureAwait(false);
        var categoryTotals = await _reader.GetCategoryTotalsAsync(cancellationToken).ConfigureAwait(false);
        var global = await _reader.GetGlobalCountsAsync(cancellationToken).ConfigureAwait(false);
        var volumes = await _volumes.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var pending = await _journal.GetPendingReplicationOperationsAsync(cancellationToken).ConfigureAwait(false);

        var presenceByVolume = presence
            .GroupBy(p => p.VolumeId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(p => p.Category, p => p));
        var totalByCategory = categoryTotals.ToDictionary(t => t.Category, t => t.LogicalFiles);

        var scopeCache = new Dictionary<string, ScopeExpression>(StringComparer.Ordinal);
        var volumeInventories = new List<VolumeInventory>();
        foreach (var volume in volumes.Where(v => v.Kind != VolumeKind.Default))
        {
            var expression = await ResolveScopeAsync(volume.ReplicationScopeId, scopeCache, cancellationToken).ConfigureAwait(false);
            var hasScope = expression is not NoneScope;
            var inScopeCategories = Categories.Where(c => expression.Matches(new ScopeSubject(c))).ToHashSet();
            presenceByVolume.TryGetValue(volume.Id, out var cellsByCategory);

            var cells = new List<VolumeCategoryCell>();
            long space = 0;
            int gapTotal = 0, surplusTotal = 0;
            foreach (var category in Categories)
            {
                var present = cellsByCategory is not null && cellsByCategory.TryGetValue(category, out var cell) ? cell.Count : 0;
                space += cellsByCategory is not null && cellsByCategory.TryGetValue(category, out var c2) ? c2.SpaceBytes : 0;

                var inScope = hasScope && inScopeCategories.Contains(category)
                    ? totalByCategory.GetValueOrDefault(category, 0)
                    : 0;
                var gap = inScope > present ? inScope - present : 0;
                var surplus = hasScope && !inScopeCategories.Contains(category) ? present : 0;
                gapTotal += gap;
                surplusTotal += surplus;

                if (present > 0 || inScope > 0)
                {
                    cells.Add(new VolumeCategoryCell(category, present, inScope, gap, surplus));
                }
            }

            volumeInventories.Add(new VolumeInventory(
                volume.Id, volume.Name, volume.Status, hasScope, space, gapTotal, surplusTotal, cells));
        }

        var categorySummaries = categoryTotals
            .OrderByDescending(t => t.SpaceBytes)
            .Select(t => new CategorySummary(t.Category, t.LogicalFiles, t.SpaceBytes))
            .ToList();

        var health = BuildHealth(volumes, pending.Count);

        return new InventorySnapshot(volumeInventories, categorySummaries, global, health);
    }

    private static InventoryHealth BuildHealth(IReadOnlyList<VolumeRecord> volumes, int pendingOperations)
    {
        var now = DateTime.UtcNow;
        var enrolled = volumes.Where(v => v.Kind != VolumeKind.Default).ToList();
        var offline = enrolled.Count(v => v.Status == VolumeStatus.Offline);
        var stale = enrolled.Count(v => v.LastScanAt is { } scan && now - scan > StaleScanAfter);
        var oldestScan = enrolled
            .Where(v => v.LastScanAt is not null)
            .Select(v => v.LastScanAt!.Value)
            .DefaultIfEmpty()
            .Min();
        return new InventoryHealth(offline, stale, pendingOperations, oldestScan == default ? null : oldestScan);
    }

    private async Task<ScopeExpression> ResolveScopeAsync(
        string? scopeId, IDictionary<string, ScopeExpression> cache, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(scopeId))
        {
            return ScopeExpression.None;
        }

        if (cache.TryGetValue(scopeId, out var cached))
        {
            return cached;
        }

        var scope = await _scopes.GetAsync(new ScopeId(scopeId), cancellationToken).ConfigureAwait(false);
        var expression = scope?.Expression ?? ScopeExpression.None;
        cache[scopeId] = expression;
        return expression;
    }
}

/// <summary>Instantané complet du tableau de bord inventaire.</summary>
public sealed record InventorySnapshot(
    IReadOnlyList<VolumeInventory> Volumes,
    IReadOnlyList<CategorySummary> Categories,
    GlobalInventoryCounts Global,
    InventoryHealth Health);

/// <summary>Ligne du tableau de bord pour un volume : cellules par catégorie + totaux gap/surplus.</summary>
public sealed record VolumeInventory(
    VolumeId Id, string Name, VolumeStatus Status, bool HasScope,
    long SpaceBytes, int GapCount, int SurplusCount, IReadOnlyList<VolumeCategoryCell> Cells);

/// <summary>Cellule volume × catégorie : présent, en-scope, manque (à copier), surplus (hors scope).</summary>
public sealed record VolumeCategoryCell(MediaCategory Category, int Present, int InScope, int Gap, int Surplus);

/// <summary>Synthèse par catégorie sur l'ensemble du catalogue.</summary>
public sealed record CategorySummary(MediaCategory Category, int LogicalFiles, long SpaceBytes);

/// <summary>Indicateurs de santé de la bibliothèque.</summary>
public sealed record InventoryHealth(int OfflineVolumes, int StaleVolumes, int PendingOperations, DateTime? OldestScan);
