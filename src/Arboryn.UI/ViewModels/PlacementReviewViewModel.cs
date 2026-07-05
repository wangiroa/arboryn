using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Arboryn.Application.Abstractions;
using Arboryn.Application.Replication;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// ViewModel de l'écran « Plan de placement » (Inc 10, § 5.5) : génère le plan de convergence
/// multi-volume, en présente la synthèse par volume (copies / déplacements / suppressions,
/// impact espace) et le détail par opération (désactivables), signale les conflits, puis exécute
/// le sous-ensemble retenu — avec annulation du dernier lot.
/// </summary>
public sealed class PlacementReviewViewModel : INotifyPropertyChanged
{
    private readonly BuildReplicationPlanHandler _planBuilder;
    private readonly ExecuteReplicationPlanHandler _executor;
    private readonly UndoReplicationBatchHandler _undo;
    private readonly IVolumeRepository _volumes;
    private readonly ILogger<PlacementReviewViewModel> _logger;

    private PlacementPlan? _plan;
    private BatchId? _lastBatch;
    private bool _isBusy;
    private string _statusText = "Générez le plan pour voir ce qui sera copié, déplacé ou supprimé sur chaque volume.";

    public PlacementReviewViewModel(
        BuildReplicationPlanHandler planBuilder,
        ExecuteReplicationPlanHandler executor,
        UndoReplicationBatchHandler undo,
        IVolumeRepository volumes,
        ILogger<PlacementReviewViewModel> logger)
    {
        _planBuilder = planBuilder;
        _executor = executor;
        _undo = undo;
        _volumes = volumes;
        _logger = logger;
    }

    public ObservableCollection<PlacementVolumeSummary> VolumeSummaries { get; } = new();

    public ObservableCollection<PlacementOperationRow> Operations { get; } = new();

    public ObservableCollection<PlacementConflictRow> Conflicts { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanGenerate));
                OnPropertyChanged(nameof(CanExecute));
                OnPropertyChanged(nameof(CanUndo));
            }
        }
    }

    public bool CanGenerate => !_isBusy;

    public bool CanExecute => !_isBusy && SelectedCount > 0;

    public bool CanUndo => !_isBusy && _lastBatch is not null;

    public bool HasPlan => Operations.Count > 0 || Conflicts.Count > 0;

    public bool HasConflicts => Conflicts.Count > 0;

    public int SelectedCount => Operations.Count(o => o.IsSelected);

    public string SelectionSummary => $"{SelectedCount}/{Operations.Count} opération(s) sélectionnée(s)";

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public async Task GenerateAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Calcul du plan de placement…";
        try
        {
            var names = (await _volumes.GetAllAsync(cancellationToken).ConfigureAwait(true))
                .ToDictionary(v => v.Id, v => v.Name);
            var plan = await Task.Run(() => _planBuilder.ExecuteAsync(cancellationToken), cancellationToken);
            _plan = plan;

            ClearOperations();
            foreach (var op in plan.Operations)
            {
                var row = new PlacementOperationRow(op, names);
                row.PropertyChanged += OnRowChanged;
                Operations.Add(row);
            }

            VolumeSummaries.Clear();
            foreach (var (volumeId, delta) in plan.SpaceDeltaByVolume.OrderBy(kv => Name(names, kv.Key)))
            {
                VolumeSummaries.Add(BuildSummary(volumeId, Name(names, volumeId), delta, plan.Operations));
            }

            Conflicts.Clear();
            foreach (var conflict in plan.Conflicts)
            {
                Conflicts.Add(new PlacementConflictRow(conflict, names));
            }

            RefreshSelectionState();
            OnPropertyChanged(nameof(HasConflicts));
            StatusText = Describe(plan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du calcul du plan de placement");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (_plan is null || !CanExecute)
        {
            return;
        }

        var selected = Operations.Where(o => o.IsSelected).Select(o => o.Operation).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Aucune opération sélectionnée.";
            return;
        }

        IsBusy = true;
        StatusText = "Exécution du plan…";
        try
        {
            var subset = new PlacementPlan(selected, _plan.Conflicts, _plan.SpaceDeltaByVolume, _plan.SkippedUnplaceable);
            var result = await Task.Run(() => _executor.ExecuteAsync(subset, cancellationToken), cancellationToken);
            _lastBatch = result.Copied + result.Moved + result.Deleted > 0 ? result.BatchId : null;

            ClearOperations();
            VolumeSummaries.Clear();
            _plan = null;

            StatusText = $"{result.Copied} copiée(s), {result.Moved} déplacée(s), {result.Deleted} supprimée(s)"
                + (result.Pending > 0 ? $", {result.Pending} différée(s) (volume hors-ligne)" : string.Empty)
                + (result.Failed > 0 ? $", {result.Failed} échec(s)" : string.Empty)
                + (_lastBatch is not null ? " — annulable ci-dessous." : ".");
            RefreshSelectionState();
            OnPropertyChanged(nameof(CanUndo));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'exécution du plan de placement");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UndoAsync(CancellationToken cancellationToken = default)
    {
        if (_lastBatch is not { } batch || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Annulation du dernier lot de réplication…";
        try
        {
            var result = await Task.Run(() => _undo.ExecuteAsync(batch, cancellationToken), cancellationToken);
            _lastBatch = null;
            StatusText = result.Failed == 0
                ? $"{result.Undone} opération(s) annulée(s)."
                : $"{result.Undone} annulée(s), {result.Failed} échec(s) — voir les logs.";
            OnPropertyChanged(nameof(CanUndo));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'annulation du lot de réplication");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Name(IReadOnlyDictionary<VolumeId, string> names, VolumeId id)
        => names.TryGetValue(id, out var name) ? name : id.Value[..Math.Min(8, id.Value.Length)];

    private static PlacementVolumeSummary BuildSummary(
        VolumeId volumeId, string name, long delta, IReadOnlyList<PlacementOperation> operations)
    {
        var copies = operations.Count(o => o.Kind == OperationKind.Copy && o.TargetVolumeId == volumeId);
        var moves = operations.Count(o => (o.Kind == OperationKind.Move || o.Kind == OperationKind.Rename) && o.SourceVolumeId == volumeId);
        var deletes = operations.Count(o => o.Kind == OperationKind.Delete && o.SourceVolumeId == volumeId);
        return new PlacementVolumeSummary(name, copies, moves, deletes, FormatSignedBytes(delta));
    }

    private static string Describe(PlacementPlan plan)
    {
        if (plan.Operations.Count == 0 && plan.Conflicts.Count == 0)
        {
            return "Tout est déjà en place : aucune opération nécessaire.";
        }

        var conflictNote = plan.Conflicts.Count > 0
            ? $" {plan.Conflicts.Count} conflit(s) de version à résoudre manuellement."
            : string.Empty;
        var skippedNote = plan.SkippedUnplaceable > 0
            ? $" {plan.SkippedUnplaceable} œuvre(s) non plaçable(s) (métadonnées insuffisantes)."
            : string.Empty;
        return $"{plan.Operations.Count} opération(s) proposée(s).{conflictNote}{skippedNote}";
    }

    private void ClearOperations()
    {
        foreach (var row in Operations)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Operations.Clear();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlacementOperationRow.IsSelected))
        {
            RefreshSelectionState();
        }
    }

    private void RefreshSelectionState()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(CanExecute));
    }

    internal static string FormatSignedBytes(long bytes)
    {
        var sign = bytes > 0 ? "+" : bytes < 0 ? "−" : string.Empty;
        var abs = Math.Abs(bytes);
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        double size = abs;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{sign}{size.ToString("0.#", CultureInfo.GetCultureInfo("fr-FR"))} {units[unit]}";
    }

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

/// <summary>Synthèse d'un volume dans le plan : entrées, mouvements, sorties, impact espace.</summary>
public sealed class PlacementVolumeSummary
{
    public PlacementVolumeSummary(string name, int copies, int moves, int deletes, string spaceText)
    {
        Name = name;
        Detail = $"{copies} copie(s) entrante(s) · {moves} déplacement(s) · {deletes} suppression(s)";
        SpaceText = spaceText;
    }

    public string Name { get; }

    public string Detail { get; }

    public string SpaceText { get; }
}

/// <summary>Ligne d'opération désactivable du plan.</summary>
public sealed class PlacementOperationRow : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public PlacementOperationRow(PlacementOperation operation, IReadOnlyDictionary<VolumeId, string> names)
    {
        Operation = operation;
        KindLabel = operation.Kind switch
        {
            OperationKind.Copy => "Copier",
            OperationKind.Move => "Déplacer",
            OperationKind.Rename => "Renommer",
            OperationKind.Delete => "Supprimer",
            _ => operation.Kind.ToString(),
        };
        var source = Resolve(names, operation.SourceVolumeId);
        VolumeLabel = operation.Kind == OperationKind.Copy
            ? $"{source} → {Resolve(names, operation.TargetVolumeId)}"
            : source;
        PathLabel = operation.Kind == OperationKind.Delete
            ? operation.OldRelativePath ?? operation.NewRelativePath
            : operation.NewRelativePath;
    }

    public PlacementOperation Operation { get; }

    public string KindLabel { get; }

    public string VolumeLabel { get; }

    public string PathLabel { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    private static string Resolve(IReadOnlyDictionary<VolumeId, string> names, VolumeId id)
        => names.TryGetValue(id, out var name) ? name : id.Value[..Math.Min(8, id.Value.Length)];

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Ligne de conflit (versions divergentes du même fichier).</summary>
public sealed class PlacementConflictRow
{
    public PlacementConflictRow(PlacementConflict conflict, IReadOnlyDictionary<VolumeId, string> names)
    {
        Description = conflict.Description;
        Volumes = string.Join(", ", conflict.Volumes.Select(v => names.TryGetValue(v, out var n) ? n : v.Value[..Math.Min(8, v.Value.Length)]));
    }

    public string Description { get; }

    public string Volumes { get; }
}
