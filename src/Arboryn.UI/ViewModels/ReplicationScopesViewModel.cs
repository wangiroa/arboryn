using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Replication;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// ViewModel de l'écran « Réplication » (Inc 10) : rattache à chaque volume enrôlé un périmètre
/// (<see cref="ScopeExpression"/>) — tout, un ensemble de catégories, ou rien. Le périmètre
/// pilote ensuite le plan de placement (« Plan de placement »).
/// </summary>
public sealed class ReplicationScopesViewModel : INotifyPropertyChanged
{
    private readonly IVolumeRepository _volumes;
    private readonly IReplicationScopeRepository _scopes;
    private readonly ILogger<ReplicationScopesViewModel> _logger;

    private bool _isBusy;
    private string _statusText = "Définissez, pour chaque volume, le périmètre de contenu qu'il doit répliquer.";

    public ReplicationScopesViewModel(
        IVolumeRepository volumes,
        IReplicationScopeRepository scopes,
        ILogger<ReplicationScopesViewModel> logger)
    {
        _volumes = volumes;
        _scopes = scopes;
        _logger = logger;
    }

    public ObservableCollection<VolumeScopeRow> Volumes { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanInteract));
            }
        }
    }

    public bool CanInteract => !_isBusy;

    public bool HasVolumes => Volumes.Count > 0;

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var volumes = await _volumes.GetAllAsync(cancellationToken).ConfigureAwait(true);
            var scopes = (await _scopes.GetAllAsync(cancellationToken).ConfigureAwait(true))
                .ToDictionary(s => s.Id.Value, s => s);

            Volumes.Clear();
            foreach (var volume in volumes.Where(v => v.Kind != VolumeKind.Default))
            {
                ReplicationScope? scope = volume.ReplicationScopeId is { } id && scopes.TryGetValue(id, out var s) ? s : null;
                var categories = scope?.Expression is CategoryScope cs ? cs.Values.ToList() : new List<MediaCategory>();
                Volumes.Add(new VolumeScopeRow(volume.Id, volume.Name, Summarize(scope?.Expression))
                {
                    IsAll = scope?.Expression is AllScope,
                    Categories = categories,
                });
            }

            OnPropertyChanged(nameof(HasVolumes));
            StatusText = Volumes.Count == 0
                ? "Aucun volume enrôlé. Enrôlez d'abord vos disques et NAS depuis l'écran « Volumes »."
                : $"{Volumes.Count} volume(s). Un volume sans périmètre ne reçoit aucune réplication.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du chargement des périmètres de réplication");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Définit le périmètre d'un volume : <paramref name="expression"/> = <c>null</c> détache le
    /// volume (rien à répliquer). Réutilise le scope existant du volume s'il y en a un, sinon en crée un.
    /// </summary>
    public async Task SetScopeAsync(VolumeScopeRow row, ScopeExpression? expression, CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var volume = await _volumes.GetAsync(row.Id, cancellationToken).ConfigureAwait(true);
            if (volume is null)
            {
                StatusText = "Volume introuvable.";
                return;
            }

            if (expression is null)
            {
                await _volumes.UpsertAsync(volume with { ReplicationScopeId = null }, cancellationToken).ConfigureAwait(true);
                StatusText = $"Volume « {row.Name} » : aucun périmètre (rien ne sera répliqué).";
            }
            else
            {
                var scopeId = volume.ReplicationScopeId is { Length: > 0 } existing ? new ScopeId(existing) : ScopeId.New();
                await _scopes.UpsertAsync(new ReplicationScope(scopeId, row.Name, expression), cancellationToken).ConfigureAwait(true);
                await _volumes.UpsertAsync(volume with { ReplicationScopeId = scopeId.Value }, cancellationToken).ConfigureAwait(true);
                StatusText = $"Périmètre de « {row.Name} » mis à jour : {Summarize(expression)}.";
            }

            await LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de la mise à jour du périmètre du volume {Volume}", row.Id.Value);
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Résumé lisible d'une expression de scope (les cas produits par cet éditeur).</summary>
    private static string Summarize(ScopeExpression? expression) => expression switch
    {
        null or NoneScope => "Aucun périmètre",
        AllScope => "Tout le contenu",
        CategoryScope c => string.Join(", ", c.Values.Select(CategoryLabels.Of)),
        _ => "Expression personnalisée",
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Ligne d'affichage d'un volume et de son périmètre de réplication.</summary>
public sealed class VolumeScopeRow
{
    public VolumeScopeRow(VolumeId id, string name, string scopeSummary)
    {
        Id = id;
        Name = name;
        ScopeSummary = scopeSummary;
    }

    public VolumeId Id { get; }

    public string Name { get; }

    public string ScopeSummary { get; }

    /// <summary>Le périmètre courant est « Tout » (pré-sélectionne le mode dans l'éditeur).</summary>
    public bool IsAll { get; init; }

    /// <summary>Catégories cochées à l'ouverture de l'éditeur.</summary>
    public IReadOnlyList<MediaCategory> Categories { get; init; } = Array.Empty<MediaCategory>();
}
