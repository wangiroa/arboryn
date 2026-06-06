using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// ViewModel de l'écran d'uniformisation (Inc 6) : choisir un dossier, prévisualiser le
/// plan Avant→Après (rename/move, conflits résolus), exécuter, puis annuler le dernier lot.
/// </summary>
public sealed class NormalizeViewModel : INotifyPropertyChanged
{
    private readonly PlanUniformizationHandler _planner;
    private readonly ExecuteUniformizationHandler _executor;
    private readonly UndoUniformizationHandler _undo;
    private readonly ILogger<NormalizeViewModel> _logger;

    private string _currentFolder = "(aucun dossier sélectionné)";
    private string _statusText = "Choisissez un dossier à uniformiser, puis lancez un aperçu.";
    private bool _isBusy;
    private string? _selectedFolder;
    private UniformizationPlan? _plan;

    public NormalizeViewModel(
        PlanUniformizationHandler planner,
        ExecuteUniformizationHandler executor,
        UndoUniformizationHandler undo,
        ILogger<NormalizeViewModel> logger)
    {
        _planner = planner;
        _executor = executor;
        _undo = undo;
        _logger = logger;
    }

    /// <summary>Opérations proposées par le dernier aperçu (vide après exécution).</summary>
    public ObservableCollection<PlannedOperationItem> Operations { get; } = new();

    public string CurrentFolder
    {
        get => _currentFolder;
        private set => SetProperty(ref _currentFolder, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanPlan));
                OnPropertyChanged(nameof(CanExecute));
                OnPropertyChanged(nameof(CanUndo));
            }
        }
    }

    public bool CanPlan => !_isBusy && _selectedFolder is not null;

    public bool CanExecute => !_isBusy && Operations.Count > 0;

    public bool CanUndo => !_isBusy;

    /// <summary>Nombre d'opérations en attente (pour la confirmation avant exécution).</summary>
    public int PendingOperationCount => Operations.Count;

    public void SelectFolder(string folderPath)
    {
        _selectedFolder = folderPath;
        CurrentFolder = folderPath;
        Operations.Clear();
        _plan = null;
        OnPropertyChanged(nameof(CanPlan));
        OnPropertyChanged(nameof(CanExecute));
        StatusText = "Dossier sélectionné. Lancez un aperçu pour voir le plan.";
    }

    public async Task PlanAsync()
    {
        if (!CanPlan)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Analyse des chemins canoniques…";
        try
        {
            var root = FilePath.From(_selectedFolder!);
            var plan = await Task.Run(() => _planner.ExecuteAsync(VolumeId.Default, root));
            _plan = plan;

            Operations.Clear();
            foreach (var operation in plan.Operations)
            {
                Operations.Add(new PlannedOperationItem(operation, root.Value));
            }

            OnPropertyChanged(nameof(CanExecute));
            StatusText = plan.Operations.Count == 0
                ? $"Tout est déjà conforme. {plan.AlreadyCanonical} fichier(s) en place, {plan.Skipped} ignoré(s) (métadonnées insuffisantes)."
                : $"{plan.Operations.Count} opération(s) proposée(s) — {plan.AlreadyCanonical} déjà conforme(s), {plan.Skipped} ignoré(s) (métadonnées insuffisantes).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'aperçu d'uniformisation");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExecuteAsync()
    {
        if (_plan is null || Operations.Count == 0 || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Uniformisation en cours…";
        try
        {
            var result = await Task.Run(() => _executor.ExecuteAsync(_plan));
            Operations.Clear();
            _plan = null;
            OnPropertyChanged(nameof(CanExecute));

            StatusText = result.Failed == 0
                ? $"{result.Moved} fichier(s) uniformisé(s). Annulable via « Annuler la dernière uniformisation »."
                : $"{result.Moved} uniformisé(s), {result.Failed} échec(s) — voir les logs.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'exécution de l'uniformisation");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UndoAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Annulation du dernier lot…";
        try
        {
            var result = await Task.Run(() => _undo.ExecuteAsync());
            StatusText = !result.HadBatch
                ? "Rien à annuler."
                : result.Failed == 0
                    ? $"{result.Restored} fichier(s) remis à leur emplacement d'origine."
                    : $"{result.Restored} remis, {result.Failed} échec(s).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'annulation de l'uniformisation");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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

/// <summary>Projection bindable d'une <see cref="PlannedOperation"/> pour la liste d'aperçu.</summary>
public sealed class PlannedOperationItem
{
    public string KindLabel { get; }

    public string OldPath { get; }

    /// <summary>Chemin cible, relatif à la racine de bibliothèque (plus lisible).</summary>
    public string NewRelativePath { get; }

    public PlannedOperationItem(PlannedOperation operation, string libraryRoot)
    {
        KindLabel = operation.Kind == OperationKind.Rename ? "Renommer" : "Déplacer";
        OldPath = operation.OldPath.Value;
        NewRelativePath = Relativize(operation.NewPath.Value, libraryRoot);
    }

    private static string Relativize(string path, string root)
        => path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? path[root.Length..].TrimStart('\\', '/')
            : path;
}
