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
/// Détail gap/surplus d'un volume (Inc 11, § 5.9) : les œuvres en scope absentes du volume (à
/// copier) et les instances présentes hors scope (à supprimer/déplacer). Bornés pour rester légers.
/// </summary>
public sealed class VolumeDrillDownHandler
{
    private const int Limit = 300;

    private static readonly IReadOnlyList<MediaCategory> AllCategories = Enum.GetValues<MediaCategory>()
        .Where(c => c != MediaCategory.Unknown)
        .ToList();

    private readonly IInventoryReader _reader;
    private readonly IVolumeRepository _volumes;
    private readonly IReplicationScopeRepository _scopes;

    public VolumeDrillDownHandler(
        IInventoryReader reader, IVolumeRepository volumes, IReplicationScopeRepository scopes)
    {
        _reader = reader;
        _volumes = volumes;
        _scopes = scopes;
    }

    public async Task<VolumeDrillDown> ExecuteAsync(VolumeId volumeId, CancellationToken cancellationToken = default)
    {
        var volume = await _volumes.GetAsync(volumeId, cancellationToken).ConfigureAwait(false);
        var expression = await ResolveScopeAsync(volume?.ReplicationScopeId, cancellationToken).ConfigureAwait(false);
        var inScope = AllCategories.Where(c => expression.Matches(new ScopeSubject(c))).ToList();

        var missing = await _reader.GetMissingAsync(volumeId, inScope, Limit, cancellationToken).ConfigureAwait(false);
        var surplus = await _reader.GetSurplusAsync(volumeId, inScope, Limit, cancellationToken).ConfigureAwait(false);
        return new VolumeDrillDown(missing, surplus);
    }

    private async Task<ScopeExpression> ResolveScopeAsync(string? scopeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(scopeId))
        {
            return ScopeExpression.None;
        }

        var scope = await _scopes.GetAsync(new ScopeId(scopeId), cancellationToken).ConfigureAwait(false);
        return scope?.Expression ?? ScopeExpression.None;
    }
}

/// <summary>Détail d'un volume : œuvres manquantes (à copier) et surplus (à retirer).</summary>
public sealed record VolumeDrillDown(
    IReadOnlyList<InventoryWorkItem> Missing, IReadOnlyList<InventoryWorkItem> Surplus);
