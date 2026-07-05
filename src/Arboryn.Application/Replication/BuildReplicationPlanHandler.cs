using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Replication;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Replication;

/// <summary>
/// Génère le plan de placement complet (Inc 10, § 5.5) depuis la base : résout le périmètre de
/// chaque volume enrôlé, assemble le catalogue logique, puis délègue le diff au
/// <see cref="PlacementPlanCalculator"/>. Le volume « default » (pré-multi-volume) est exclu :
/// la réplication ne concerne que les volumes réellement enrôlés.
/// </summary>
public sealed class BuildReplicationPlanHandler
{
    private readonly IVolumeRepository _volumes;
    private readonly IReplicationScopeRepository _scopes;
    private readonly BuildReplicationCatalogHandler _catalog;
    private readonly PlacementPlanCalculator _calculator;

    public BuildReplicationPlanHandler(
        IVolumeRepository volumes,
        IReplicationScopeRepository scopes,
        BuildReplicationCatalogHandler catalog,
        PlacementPlanCalculator calculator)
    {
        _volumes = volumes;
        _scopes = scopes;
        _catalog = catalog;
        _calculator = calculator;
    }

    public async Task<PlacementPlan> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var allVolumes = await _volumes.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var participating = allVolumes.Where(v => v.Kind != VolumeKind.Default).ToList();
        if (participating.Count == 0)
        {
            return PlacementPlan.Empty;
        }

        var scopeCache = new Dictionary<string, ScopeExpression>(StringComparer.Ordinal);
        var volumeScopes = new List<VolumeScope>();
        foreach (var volume in participating)
        {
            volumeScopes.Add(new VolumeScope(
                volume.Id, volume.Name, volume.Status,
                await ResolveScopeAsync(volume.ReplicationScopeId, scopeCache, cancellationToken).ConfigureAwait(false)));
        }

        // Racine enrôlée de chaque volume (mount_point) : sert à rendre relatifs les chemins
        // absolus des instances pour les comparer au chemin canonique.
        var volumeRoots = participating.ToDictionary(v => v.Id, v => v.MountPoint);
        var catalog = await _catalog.BuildAsync(volumeRoots, cancellationToken).ConfigureAwait(false);

        return _calculator.Calculate(catalog, volumeScopes);
    }

    /// <summary>Charge l'expression de scope d'un volume ; <see cref="ScopeExpression.None"/> si non défini ou introuvable.</summary>
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
