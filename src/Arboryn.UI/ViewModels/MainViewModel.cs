using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// ViewModel de l'écran principal : orchestre scan puis détection des doublons
/// exacts sur le volume par défaut, et expose l'avancement et les groupes trouvés.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private const string PriorityDirectoriesKey = "priority_directories";
    private const string PreferDeeperKey = "prefer_deeper";
    private const string FuzzyThresholdKey = "fuzzy_threshold";

    private readonly ScanDirectoryHandler _scanHandler;
    private readonly DetectExactDuplicatesHandler _detectHandler;
    private readonly DetectFuzzyDuplicatesHandler _fuzzyHandler;
    private readonly ConfirmByHashHandler _hashHandler;
    private readonly PromoteByHashHandler _promoteHandler;
    private readonly ClearCatalogHandler _clearHandler;
    private readonly DeleteFilesHandler _deleteHandler;
    private readonly UndoLastBatchHandler _undoHandler;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<MainViewModel> _logger;

    private string _statusText = "Choisissez un dossier à analyser.";
    private string _currentFolder = "(aucun dossier analysé)";
    private bool _isScanning;
    private bool _detectWholeCatalog;
    private bool _preferDeeper = true;
    private double _fuzzyThreshold = 0.85;
    private CancellationTokenSource? _cts;
    private FilePath? _lastRoot;

    public MainViewModel(
        ScanDirectoryHandler scanHandler,
        DetectExactDuplicatesHandler detectHandler,
        DetectFuzzyDuplicatesHandler fuzzyHandler,
        ConfirmByHashHandler hashHandler,
        PromoteByHashHandler promoteHandler,
        ClearCatalogHandler clearHandler,
        DeleteFilesHandler deleteHandler,
        UndoLastBatchHandler undoHandler,
        ISettingsRepository settings,
        ILogger<MainViewModel> logger)
    {
        _scanHandler = scanHandler;
        _detectHandler = detectHandler;
        _fuzzyHandler = fuzzyHandler;
        _hashHandler = hashHandler;
        _promoteHandler = promoteHandler;
        _clearHandler = clearHandler;
        _deleteHandler = deleteHandler;
        _undoHandler = undoHandler;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Charge les préférences de priorité depuis la base. À appeler une fois au
    /// démarrage, avant l'affichage de la fenêtre.
    /// </summary>
    public async Task LoadSettingsAsync()
    {
        try
        {
            var deeper = await _settings.GetAsync(PreferDeeperKey, CancellationToken.None);
            if (deeper is not null)
            {
                _preferDeeper = deeper == "true";
                OnPropertyChanged(nameof(PreferDeeper));
            }

            var threshold = await _settings.GetAsync(FuzzyThresholdKey, CancellationToken.None);
            if (threshold is not null &&
                double.TryParse(threshold, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                _fuzzyThreshold = parsed;
            }

            var json = await _settings.GetAsync(PriorityDirectoriesKey, CancellationToken.None);
            if (json is not null)
            {
                var directories = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                PriorityDirectories.Clear();
                foreach (var directory in directories)
                {
                    PriorityDirectories.Add(directory);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec du chargement des préférences de priorité");
        }
    }

    private void PersistPrioritySettings()
    {
        _ = PersistPrioritySettingsAsync();
    }

    private async Task PersistPrioritySettingsAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(PriorityDirectories.ToList());
            await _settings.SetAsync(PriorityDirectoriesKey, json, CancellationToken.None);
            await _settings.SetAsync(PreferDeeperKey, _preferDeeper ? "true" : "false", CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec de la sauvegarde des préférences de priorité");
        }
    }

    public ObservableCollection<DuplicateGroupItem> Groups { get; } = new();

    /// <summary>Répertoires prioritaires, du plus prioritaire au moins. Ordre = hiérarchie.</summary>
    public ObservableCollection<string> PriorityDirectories { get; } = new();

    /// <summary>Répertoires récurrents suggérés (présents dans plusieurs groupes), hors priorités déjà choisies.</summary>
    public ObservableCollection<string> SuggestedDirectories { get; } = new();

    /// <summary>À rang de priorité égal, conserver l'arborescence la plus profonde (mieux rangée).</summary>
    public bool PreferDeeper
    {
        get => _preferDeeper;
        set
        {
            if (SetProperty(ref _preferDeeper, value))
            {
                ApplyPrioritySelectionToGroups();
                PersistPrioritySettings();
            }
        }
    }

    /// <summary>Ajoute un répertoire prioritaire (en dernière position) et ré-applique la sélection.</summary>
    public void AddPriorityDirectory(string directory)
    {
        var normalized = NormalizeDirectory(directory);
        if (normalized.Length == 0 ||
            PriorityDirectories.Any(d => string.Equals(d, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        PriorityDirectories.Add(normalized);

        var suggestion = SuggestedDirectories.FirstOrDefault(d => string.Equals(d, normalized, StringComparison.OrdinalIgnoreCase));
        if (suggestion is not null)
        {
            SuggestedDirectories.Remove(suggestion);
        }

        ApplyPrioritySelectionToGroups();
        PersistPrioritySettings();
    }

    public void RemovePriorityDirectory(string directory)
    {
        if (PriorityDirectories.Remove(directory))
        {
            ApplyPrioritySelectionToGroups();
            PersistPrioritySettings();
        }
    }

    public void MovePriorityUp(string directory)
    {
        var index = PriorityDirectories.IndexOf(directory);
        if (index > 0)
        {
            PriorityDirectories.Move(index, index - 1);
            ApplyPrioritySelectionToGroups();
            PersistPrioritySettings();
        }
    }

    public void MovePriorityDown(string directory)
    {
        var index = PriorityDirectories.IndexOf(directory);
        if (index >= 0 && index < PriorityDirectories.Count - 1)
        {
            PriorityDirectories.Move(index, index + 1);
            ApplyPrioritySelectionToGroups();
            PersistPrioritySettings();
        }
    }

    private static string NormalizeDirectory(string directory)
    {
        try
        {
            return FilePath.From(directory).Value;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(CanScan));
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(CanRescan));
            }
        }
    }

    /// <summary>Dossier analysé en dernier (affiché à l'écran).</summary>
    public string CurrentFolder
    {
        get => _currentFolder;
        private set => SetProperty(ref _currentFolder, value);
    }

    /// <summary>Le bouton de scan est actif tant qu'aucun scan n'est en cours.</summary>
    public bool CanScan => !_isScanning;

    /// <summary>Le bouton d'annulation est actif uniquement pendant un scan.</summary>
    public bool CanCancel => _isScanning;

    /// <summary>Re-scan possible si un dossier a déjà été analysé et qu'aucun scan n'est en cours.</summary>
    public bool CanRescan => !_isScanning && _lastRoot is not null;

    /// <summary>Relance le scan du dernier dossier analysé.</summary>
    public Task RescanAsync()
        => _lastRoot is { } root ? ScanAndDetectAsync(root.Value) : Task.CompletedTask;

    /// <summary>
    /// Portée de la détection : <c>false</c> = uniquement les fichiers du scan courant ;
    /// <c>true</c> = tout le catalogue du volume.
    /// </summary>
    public bool DetectWholeCatalog
    {
        get => _detectWholeCatalog;
        set => SetProperty(ref _detectWholeCatalog, value);
    }

    /// <summary>Demande l'arrêt du scan en cours (si présent).</summary>
    public void CancelScan() => _cts?.Cancel();

    /// <summary>Envoie à la corbeille les copies cochées d'un seul groupe.</summary>
    public Task DeleteSelectedAsync(DuplicateGroupItem group)
        => DeleteAsync(group.Members.Where(m => m.ShouldDelete).ToList());

    /// <summary>Envoie à la corbeille les copies cochées de tous les groupes, en un seul lot.</summary>
    public Task DeleteAllSelectedAsync()
        => DeleteAsync(Groups.SelectMany(g => g.Members).Where(m => m.ShouldDelete).ToList());

    private async Task DeleteAsync(IReadOnlyList<DuplicateMemberItem> selected)
    {
        if (IsScanning || selected.Count == 0)
        {
            return;
        }

        var toDelete = selected.Select(m => new FileToDelete(m.Id, m.Path)).ToList();

        try
        {
            var result = await Task.Run(() => _deleteHandler.ExecuteAsync(toDelete));

            // Mise à jour ciblée : on retire les copies supprimées des groupes concernés
            // SANS reconstruire la liste, pour préserver les sélections en cours.
            var deletedIds = toDelete.Select(f => f.Id.Value).ToHashSet();
            ApplyDeletionToGroups(deletedIds);

            var prefix = result.Failed == 0
                ? $"{result.Deleted} fichier(s) à la corbeille. "
                : $"{result.Deleted} supprimé(s), {result.Failed} échec(s). ";
            StatusText = prefix + RemainingSummary();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de la suppression");
            StatusText = $"Erreur : {ex.Message}";
        }
    }

    private void ApplyDeletionToGroups(ISet<string> deletedMemberIds)
    {
        for (var i = Groups.Count - 1; i >= 0; i--)
        {
            var group = Groups[i];
            if (!group.Members.Any(m => deletedMemberIds.Contains(m.Id.Value)))
            {
                continue;
            }

            var updated = group.WithoutMembers(deletedMemberIds);
            if (updated is null)
            {
                Groups.RemoveAt(i);
            }
            else
            {
                Groups[i] = updated;
            }
        }
    }

    private string RemainingSummary()
    {
        var total = Groups.Sum(g => g.ReclaimableBytes);
        return Groups.Count == 0
            ? "Plus aucun doublon."
            : $"{Groups.Count} groupe(s) restant(s) — {SizeFormatter.Humanize(total)} récupérables.";
    }

    /// <summary>Annule la dernière suppression (restaure depuis la corbeille) et rafraîchit.</summary>
    public async Task UndoLastDeleteAsync()
    {
        if (IsScanning)
        {
            return;
        }

        try
        {
            var result = await Task.Run(() => _undoHandler.ExecuteAsync());
            if (!result.HadBatch)
            {
                StatusText = "Rien à annuler.";
                return;
            }

            var prefix = result.Failed == 0
                ? $"{result.Restored} fichier(s) restauré(s). "
                : $"{result.Restored} restauré(s), {result.Failed} échec(s). ";
            await PopulateGroupsAsync(prefix);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'annulation");
            StatusText = $"Erreur : {ex.Message}";
        }
    }

    /// <summary>
    /// Recherche les doublons flous (à la demande) et les ajoute à la liste, sans
    /// toucher aux groupes exacts existants ni à leurs sélections.
    /// </summary>
    public async Task DetectFuzzyAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        StatusText = "Recherche des doublons flous…";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            var underRoot = DetectWholeCatalog ? null : _lastRoot;
            var threshold = _fuzzyThreshold;

            var views = await Task.Run(
                () => _fuzzyHandler.ExecuteAsync(VolumeId.Default, underRoot, threshold, token), token);

            // Remplace les anciens groupes flous, préserve les groupes exacts (et leurs sélections).
            for (var i = Groups.Count - 1; i >= 0; i--)
            {
                if (Groups[i].Kind == DuplicateGroupKind.FuzzyName)
                {
                    Groups.RemoveAt(i);
                }
            }

            var newItems = views.Select(v => new DuplicateGroupItem(v)).ToList();
            ApplyPrioritySelectionTo(newItems);
            foreach (var item in newItems)
            {
                Groups.Add(item);
            }

            ResortGroups();

            StatusText = newItems.Count == 0
                ? "Aucun doublon flou supplémentaire."
                : $"{newItems.Count} groupe(s) flou(s) trouvé(s). Confirmez par hash pour distinguer copies et variantes.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Recherche annulée.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de la détection floue");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsScanning = false;
        }
    }

    /// <summary>
    /// Confirme un groupe flou par hash, SANS rien retirer de la liste : chaque copie
    /// est annotée de son empreinte (les fichiers identiques affichent le même <c>#hash</c>),
    /// et la sélection est ajustée — parmi des copies identiques on garde la meilleure et
    /// on coche les autres ; les fichiers uniques (vraies variantes) sont conservés (décochés).
    /// Un re-scan rétablit l'état initial.
    /// </summary>
    public async Task ConfirmByHashAsync(DuplicateGroupItem group)
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        StatusText = "Calcul des empreintes SHA-256…";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            var targets = group.Members.Select(m => new HashTarget(m.Id, m.Path)).ToList();
            var hashGroups = await Task.Run(async () =>
            {
                var groups = await _hashHandler.ExecuteAsync(targets, token).ConfigureAwait(false);
                // Promotion : consolide les copies confirmées identiques sous un LogicalFile
                // à signature sha256, et nettoie les LFs name_size devenus orphelins.
                await _promoteHandler.ExecuteAsync(groups, token).ConfigureAwait(false);
                return groups;
            }, token);

            var membersById = group.Members.ToDictionary(m => m.Id.Value);
            var prefixes = PriorityDirectories.ToList();
            var identicalToDelete = 0;
            var distinctVariants = 0;

            foreach (var hashGroup in hashGroups)
            {
                var cluster = hashGroup.Members.Select(t => membersById[t.Id.Value]).ToList();
                var shortHash = hashGroup.Hash.Value[..8];
                foreach (var member in cluster)
                {
                    member.Hash = shortHash;
                }

                if (cluster.Count >= 2)
                {
                    // Copies byte-à-byte : on garde la meilleure, on coche les autres.
                    var candidates = cluster
                        .Select(m => new KeepCandidate(m.Directory, m.Size, System.IO.Path.GetFileName(m.DisplayPath)))
                        .ToList();
                    var keepIndex = PrioritySelection.ChooseKeepIndex(candidates, prefixes, _preferDeeper);
                    for (var i = 0; i < cluster.Count; i++)
                    {
                        cluster[i].ShouldDelete = i != keepIndex;
                    }

                    identicalToDelete += cluster.Count - 1;
                }
                else
                {
                    // Contenu unique : ce n'est pas une copie, on le conserve.
                    cluster[0].ShouldDelete = false;
                    distinctVariants++;
                }
            }

            StatusText = $"Empreintes calculées : {identicalToDelete} copie(s) identique(s) cochée(s), " +
                         $"{distinctVariants} variante(s) distincte(s) conservée(s). Re-scannez pour réinitialiser.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Confirmation annulée.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de la confirmation par hash");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsScanning = false;
        }
    }

    /// <summary>Vide le catalogue du volume par défaut.</summary>
    public async Task ClearCatalogAsync()
    {
        if (IsScanning)
        {
            return;
        }

        try
        {
            await _clearHandler.ExecuteAsync(VolumeId.Default);
            Groups.Clear();
            StatusText = "Catalogue vidé.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du vidage du catalogue");
            StatusText = $"Erreur : {ex.Message}";
        }
    }

    public async Task ScanAndDetectAsync(string folderPath)
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        Groups.Clear();
        StatusText = "Analyse en cours…";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            var root = FilePath.From(folderPath);
            _lastRoot = root;
            CurrentFolder = root.Value;
            OnPropertyChanged(nameof(CanRescan));

            // Progress créé sur le thread UI : ses rappels sont marshalés vers l'UI,
            // ce qui permet de lancer le travail lourd sur un thread de fond.
            var progress = new Progress<ScanProgress>(
                p => StatusText = $"Indexation : {p.FilesProcessed} fichiers…");

            await Task.Run(() => _scanHandler.ExecuteAsync(root, VolumeId.Default, progress, token), token);

            await PopulateGroupsAsync(cancellationToken: token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Analyse annulée.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'analyse du dossier {Folder}", folderPath);
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsScanning = false;
        }
    }

    /// <summary>
    /// (Re)détecte les doublons selon la portée courante, trie par espace récupérable
    /// décroissant, met à jour la liste et le statut (préfixé par un message d'action).
    /// </summary>
    private async Task PopulateGroupsAsync(string? prefix = null, CancellationToken cancellationToken = default)
    {
        var underRoot = DetectWholeCatalog ? null : _lastRoot;

        var views = await Task.Run(
            () => _detectHandler.ExecuteDetailedAsync(VolumeId.Default, underRoot, cancellationToken), cancellationToken);

        var ordered = views
            .Select(v => new DuplicateGroupItem(v))
            .OrderByDescending(g => g.ReclaimableBytes)
            .ToList();

        Groups.Clear();
        foreach (var item in ordered)
        {
            Groups.Add(item);
        }

        RefreshSuggestions();
        ApplyPrioritySelectionToGroups();

        var totalReclaimable = ordered.Sum(g => g.ReclaimableBytes);
        var summary = ordered.Count == 0
            ? "Aucun doublon exact trouvé."
            : $"{ordered.Count} groupe(s) — {SizeFormatter.Humanize(totalReclaimable)} récupérables.";
        StatusText = (prefix ?? string.Empty) + summary;
    }

    /// <summary>Recalcule les répertoires suggérés (récurrents, hors priorités déjà choisies).</summary>
    private void RefreshSuggestions()
    {
        var groupDirectorySets = Groups
            .Select(g => (IReadOnlyCollection<string>)g.Members.Select(m => m.Directory).ToList())
            .ToList();

        var ranked = PrioritySelection.RankDirectories(groupDirectorySets);

        SuggestedDirectories.Clear();
        foreach (var directory in ranked)
        {
            if (!PriorityDirectories.Any(d => string.Equals(d, directory, StringComparison.OrdinalIgnoreCase)))
            {
                SuggestedDirectories.Add(directory);
            }
        }
    }

    /// <summary>Coche / décoche les copies de chaque groupe selon les répertoires prioritaires ordonnés.</summary>
    private void ApplyPrioritySelectionToGroups() => ApplyPrioritySelectionTo(Groups);

    /// <summary>Applique la sélection par défaut (priorité + score) à un sous-ensemble de groupes.</summary>
    private void ApplyPrioritySelectionTo(IEnumerable<DuplicateGroupItem> groups)
    {
        var prefixes = PriorityDirectories.ToList();

        foreach (var group in groups)
        {
            var candidates = group.Members
                .Select(m => new KeepCandidate(m.Directory, m.Size, System.IO.Path.GetFileName(m.DisplayPath)))
                .ToList();
            var keepIndex = PrioritySelection.ChooseKeepIndex(candidates, prefixes, _preferDeeper);

            for (var i = 0; i < group.Members.Count; i++)
            {
                group.Members[i].ShouldDelete = i != keepIndex;
            }
        }
    }

    /// <summary>Réordonne la liste par espace récupérable décroissant (instances réutilisées).</summary>
    private void ResortGroups()
    {
        var sorted = Groups.OrderByDescending(g => g.ReclaimableBytes).ToList();
        Groups.Clear();
        foreach (var group in sorted)
        {
            Groups.Add(group);
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
