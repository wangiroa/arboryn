using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
    private IReadOnlyList<PlannedTarget> _targets = Array.Empty<PlannedTarget>();
    private FilePath? _libraryRoot;
    private bool _suspendRecompute;
    private int _recomputeGeneration;

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

    public bool CanExecute => !_isBusy && SelectedCount > 0;

    public bool CanUndo => !_isBusy;

    /// <summary>Y a-t-il des opérations à réviser (pilote l'en-tête de sélection globale) ?</summary>
    public bool HasOperations => Operations.Count > 0;

    /// <summary>Nombre d'opérations cochées (exécutées au lancement).</summary>
    public int SelectedCount
    {
        get
        {
            var n = 0;
            foreach (var op in Operations)
            {
                if (op.IsSelected)
                {
                    n++;
                }
            }
            return n;
        }
    }

    /// <summary>Nombre d'opérations à exécuter (pour la confirmation avant exécution).</summary>
    public int PendingOperationCount => SelectedCount;

    /// <summary>Résumé « N/M sélectionné(s) » affiché en en-tête.</summary>
    public string SelectionSummary => $"{SelectedCount}/{Operations.Count} sélectionné(s)";

    /// <summary>
    /// Sélection globale (lecture seule, liaison <c>OneWay</c>) : coché si toutes les opérations
    /// le sont. Mixte → affiché décoché (le résumé chiffré lève l'ambiguïté). Le basculement passe
    /// par <see cref="SetAllSelected"/> sur clic, pour éviter toute boucle de réécriture TwoWay.
    /// </summary>
    public bool AllSelected => Operations.Count > 0 && SelectedCount == Operations.Count;

    /// <summary>Coche / décoche toutes les lignes d'un coup, puis recalcule une seule fois.</summary>
    public void SetAllSelected(bool value)
    {
        _suspendRecompute = true;
        foreach (var op in Operations)
        {
            op.IsSelected = value;
        }
        _suspendRecompute = false;
        RefreshSelectionState();
        _ = RecomputeDisplayAsync();
    }

    public void SelectFolder(string folderPath)
    {
        _selectedFolder = folderPath;
        CurrentFolder = folderPath;
        ClearOperations();
        _plan = null;
        _targets = Array.Empty<PlannedTarget>();
        _libraryRoot = null;
        OnPropertyChanged(nameof(CanPlan));
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
            _libraryRoot = root;
            _targets = plan.Targets ?? Array.Empty<PlannedTarget>();

            // Opérations et cibles « à déplacer » sont produites dans le même ordre — appariées 1:1.
            ClearOperations();
            for (var i = 0; i < plan.Operations.Count; i++)
            {
                var operation = plan.Operations[i];
                var idealRelative = i < _targets.Count
                    ? _targets[i].IdealRelative
                    : PlannedOperationItem.Relativize(operation.NewPath.Value, root.Value);
                var item = new PlannedOperationItem(operation, idealRelative, root.Value);
                item.PropertyChanged += OnOperationItemChanged;
                Operations.Add(item);
            }
            RefreshSelectionState();

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
        if (_plan is null || _libraryRoot is null || IsBusy)
        {
            return;
        }

        // Recalcule les opérations sur les seules lignes cochées (suffixes recalculés en
        // conséquence) — source de vérité au moment d'exécuter, indépendante de l'aperçu.
        var selectedTargets = SelectedTargets();
        if (selectedTargets.Count == 0)
        {
            StatusText = "Aucune opération sélectionnée.";
            return;
        }

        IsBusy = true;
        StatusText = "Uniformisation en cours…";
        try
        {
            var root = _libraryRoot.Value;
            var operations = await Task.Run(() => _planner.RebuildOperations(selectedTargets, root));
            var result = await Task.Run(() => _executor.ExecuteAsync(operations));
            ClearOperations();
            _plan = null;
            _targets = Array.Empty<PlannedTarget>();

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

    /// <summary>Vide la liste d'aperçu en se désabonnant de chaque ligne, puis rafraîchit l'état.</summary>
    private void ClearOperations()
    {
        foreach (var op in Operations)
        {
            op.PropertyChanged -= OnOperationItemChanged;
        }
        Operations.Clear();
        RefreshSelectionState();
    }

    private void OnOperationItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlannedOperationItem.IsSelected) && !_suspendRecompute)
        {
            RefreshSelectionState();
            _ = RecomputeDisplayAsync();
        }
    }

    /// <summary>Cibles « à déplacer » dont la ligne est actuellement cochée.</summary>
    private List<PlannedTarget> SelectedTargets()
    {
        var selectedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in Operations)
        {
            if (op.IsSelected)
            {
                selectedIds.Add(op.Id.Value);
            }
        }
        return _targets.Where(t => selectedIds.Contains(t.Instance.Id.Value)).ToList();
    }

    /// <summary>
    /// Recalcule les chemins cibles affichés pour refléter la sélection courante : les lignes
    /// cochées reçoivent leur cible désambiguïsée parmi le sous-ensemble, les décochées affichent
    /// leur cible idéale (sans suffixe). N'exécute rien sur disque.
    /// </summary>
    private async Task RecomputeDisplayAsync()
    {
        if (_libraryRoot is null || _targets.Count == 0)
        {
            return;
        }

        var generation = ++_recomputeGeneration;
        var selectedTargets = SelectedTargets();
        var root = _libraryRoot.Value;
        var rootPath = root.Value;

        IReadOnlyList<PlannedOperation> operations = selectedTargets.Count == 0
            ? Array.Empty<PlannedOperation>()
            : await Task.Run(() => _planner.RebuildOperations(selectedTargets, root));

        // Une (dé)sélection plus récente a déjà relancé un calcul : on abandonne ce résultat périmé.
        if (generation != _recomputeGeneration)
        {
            return;
        }

        var resolvedById = operations.ToDictionary(o => o.Id.Value, o => o.NewPath.Value, StringComparer.Ordinal);
        foreach (var item in Operations)
        {
            item.NewRelativePath = resolvedById.TryGetValue(item.Id.Value, out var newPath)
                ? PlannedOperationItem.Relativize(newPath, rootPath)
                : item.IdealRelativePath;
        }
    }

    /// <summary>Notifie toutes les propriétés dérivées de l'état de sélection.</summary>
    private void RefreshSelectionState()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(HasOperations));
        OnPropertyChanged(nameof(PendingOperationCount));
        OnPropertyChanged(nameof(CanExecute));
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
public sealed class PlannedOperationItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _newRelativePath;

    /// <summary>Identifie le fichier — sert à apparier la ligne aux cibles lors du recalcul.</summary>
    public FileInstanceId Id { get; }

    public string KindLabel { get; }

    public string OldPath { get; }

    /// <summary>Cible idéale (sans suffixe), affichée quand la ligne est décochée.</summary>
    public string IdealRelativePath { get; }

    /// <summary>Chemin cible courant, relatif à la racine — recalculé à la (dé)sélection.</summary>
    public string NewRelativePath
    {
        get => _newRelativePath;
        set
        {
            if (_newRelativePath != value)
            {
                _newRelativePath = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewRelativePath)));
            }
        }
    }

    /// <summary>Inclure cette opération dans l'exécution (coché par défaut).</summary>
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

    public PlannedOperationItem(PlannedOperation operation, string idealRelative, string libraryRoot)
    {
        Id = operation.Id;
        KindLabel = operation.Kind == OperationKind.Rename ? "Renommer" : "Déplacer";
        OldPath = operation.OldPath.Value;
        IdealRelativePath = idealRelative;
        _newRelativePath = Relativize(operation.NewPath.Value, libraryRoot);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static string Relativize(string path, string root)
        => path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? path[root.Length..].TrimStart('\\', '/')
            : path;
}
