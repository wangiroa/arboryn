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
using Arboryn.UI.Services;
using Microsoft.Extensions.Logging;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// ViewModel de l'écran principal : orchestre scan puis détection des doublons
/// exacts sur le volume par défaut, et expose l'avancement et les groupes trouvés.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private const string PriorityDirectoriesKey = "priority_directories";
    private const string ExcludedDirectoriesKey = "excluded_directories";
    private const string PreferDeeperKey = "prefer_deeper";
    private const string FuzzyThresholdKey = "fuzzy_threshold";

    private readonly RescanVolumeHandler _rescanHandler;
    private readonly DetectExactDuplicatesHandler _detectHandler;
    private readonly DetectFuzzyDuplicatesHandler _fuzzyHandler;
    private readonly ConfirmByHashHandler _hashHandler;
    private readonly PromoteByHashHandler _promoteHandler;
    private readonly ComputePerceptualHashesHandler _computePerceptualHandler;
    private readonly DetectPerceptualDuplicatesHandler _detectPerceptualHandler;
    private readonly ClearCatalogHandler _clearHandler;
    private readonly DeleteFilesHandler _deleteHandler;
    private readonly UndoLastBatchHandler _undoHandler;
    private readonly EnrichDirectoryHandler _enrichHandler;
    private readonly EnrollVolumeHandler _enrollHandler;
    private readonly ActiveVolumeContext _activeVolume;
    private readonly IVolumeRepository _volumes;
    private readonly ISettingsRepository _settings;
    private readonly ILogger<MainViewModel> _logger;

    private string _statusText = "Choisissez un dossier à analyser.";
    private string _currentFolder = "(aucun dossier analysé)";
    private bool _isScanning;
    private bool _isDeleting;
    private bool _detectWholeCatalog;
    private bool _preferDeeper = true;
    private double _fuzzyThreshold = 0.85;
    private CancellationTokenSource? _cts;
    private FilePath? _lastRoot;

    // Options de détection choisies AVANT le scan ; appliquées à chaque (re-)scan.
    private bool _detectExact = true;
    private bool _detectFuzzy;
    private bool _detectPerceptual;

    // Dossier choisi (potentiellement pas encore scanné) et indicateur de scan déjà effectué.
    private string? _pendingFolderPath;
    private bool _pendingScanned;

    /// <summary>Tous les groupes détectés (source de vérité), triés par espace récupérable décroissant.</summary>
    private readonly List<DuplicateGroupItem> _allGroups = new();
    private MediaFilterOption _selectedMediaFilter;

    // Sélecteur de volume source (Inc 9) : choisir le volume dont on inspecte les doublons.
    private VolumeChoice? _selectedSourceVolume;
    private bool _suspendVolumeSync;

    public MainViewModel(
        RescanVolumeHandler rescanHandler,
        DetectExactDuplicatesHandler detectHandler,
        DetectFuzzyDuplicatesHandler fuzzyHandler,
        ConfirmByHashHandler hashHandler,
        PromoteByHashHandler promoteHandler,
        ComputePerceptualHashesHandler computePerceptualHandler,
        DetectPerceptualDuplicatesHandler detectPerceptualHandler,
        ClearCatalogHandler clearHandler,
        DeleteFilesHandler deleteHandler,
        UndoLastBatchHandler undoHandler,
        EnrichDirectoryHandler enrichHandler,
        EnrollVolumeHandler enrollHandler,
        ActiveVolumeContext activeVolume,
        IVolumeRepository volumes,
        ISettingsRepository settings,
        ILogger<MainViewModel> logger)
    {
        _rescanHandler = rescanHandler;
        _detectHandler = detectHandler;
        _fuzzyHandler = fuzzyHandler;
        _hashHandler = hashHandler;
        _promoteHandler = promoteHandler;
        _computePerceptualHandler = computePerceptualHandler;
        _detectPerceptualHandler = detectPerceptualHandler;
        _clearHandler = clearHandler;
        _deleteHandler = deleteHandler;
        _undoHandler = undoHandler;
        _enrichHandler = enrichHandler;
        _enrollHandler = enrollHandler;
        _activeVolume = activeVolume;
        _volumes = volumes;
        _settings = settings;
        _logger = logger;
        _selectedMediaFilter = MediaFilters[0];
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

            var excludedJson = await _settings.GetAsync(ExcludedDirectoriesKey, CancellationToken.None);
            if (excludedJson is not null)
            {
                var directories = JsonSerializer.Deserialize<List<string>>(excludedJson) ?? new List<string>();
                ExcludedDirectories.Clear();
                foreach (var directory in directories)
                {
                    ExcludedDirectories.Add(directory);
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
            var excludedJson = JsonSerializer.Serialize(ExcludedDirectories.ToList());
            await _settings.SetAsync(ExcludedDirectoriesKey, excludedJson, CancellationToken.None);
            await _settings.SetAsync(PreferDeeperKey, _preferDeeper ? "true" : "false", CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec de la sauvegarde des préférences de priorité");
        }
    }

    /// <summary>Groupes effectivement affichés : projection filtrée de <see cref="_allGroups"/> par type de média.</summary>
    public ObservableCollection<DuplicateGroupItem> Groups { get; } = new();

    /// <summary>Options de filtre par type de média (« Tous les types » en tête = pas de filtrage).</summary>
    public IReadOnlyList<MediaFilterOption> MediaFilters { get; } = new[]
    {
        new MediaFilterOption("Tous les types", null),
        new MediaFilterOption("Audio (musique + livres audio)", MediaFilterType.Audio),
        new MediaFilterOption("Vidéo", MediaFilterType.Video),
        new MediaFilterOption("Photos", MediaFilterType.Photo),
        new MediaFilterOption("BD", MediaFilterType.Comic),
        new MediaFilterOption("Documents bureautiques", MediaFilterType.Document),
        new MediaFilterOption("Ebooks", MediaFilterType.Ebook),
    };

    /// <summary>Filtre par type de média actif ; sa modification reconstruit la liste affichée.</summary>
    public MediaFilterOption SelectedMediaFilter
    {
        get => _selectedMediaFilter;
        set
        {
            if (value is not null && SetProperty(ref _selectedMediaFilter, value))
            {
                RebuildFilteredView();
            }
        }
    }

    /// <summary>Volumes enrôlés sélectionnables comme source de doublons (Inc 9).</summary>
    public ObservableCollection<VolumeChoice> SourceVolumes { get; } = new();

    /// <summary>
    /// Volume source dont on inspecte les doublons. Le choisir active ce volume et relance la
    /// détection sur l'ensemble de son catalogue (sans re-scan). Les changements programmatiques
    /// (sync avec le volume actif) ne déclenchent pas de détection.
    /// </summary>
    public VolumeChoice? SelectedSourceVolume
    {
        get => _selectedSourceVolume;
        set
        {
            if (SetProperty(ref _selectedSourceVolume, value) && value is not null && !_suspendVolumeSync)
            {
                _ = SwitchSourceVolumeAsync(value);
            }
        }
    }

    /// <summary>Recharge la liste des volumes et y sélectionne le volume actif (sans détection).</summary>
    public async Task LoadVolumesAsync()
    {
        try
        {
            var volumes = await _volumes.GetAllAsync(CancellationToken.None);
            _suspendVolumeSync = true;
            SourceVolumes.Clear();
            foreach (var volume in volumes)
            {
                SourceVolumes.Add(new VolumeChoice(volume.Id, volume.Name));
            }

            SelectedSourceVolume = SourceVolumes.FirstOrDefault(v => v.Id == _activeVolume.Current)
                                   ?? SourceVolumes.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du chargement des volumes pour le filtre source");
        }
        finally
        {
            _suspendVolumeSync = false;
        }
    }

    private async Task SwitchSourceVolumeAsync(VolumeChoice choice)
    {
        _activeVolume.Set(choice.Id, choice.Name);
        await ViewActiveVolumeDuplicatesAsync();
    }

    /// <summary>Affiche les doublons de tout le catalogue du volume actif (sans re-scan).</summary>
    private async Task ViewActiveVolumeDuplicatesAsync()
    {
        if (IsScanning || IsDeleting)
        {
            return;
        }

        IsScanning = true;
        _allGroups.Clear();
        RebuildFilteredView();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            await DetectSelectedAsync(wholeCatalog: true, cancellationToken: token);
            _pendingScanned = true;
            OnPropertyChanged(nameof(ScanButtonLabel));
        }
        catch (OperationCanceledException)
        {
            StatusText = "Détection annulée.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de la détection sur le volume {Volume}", _activeVolume.CurrentName);
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsScanning = false;
        }
    }

    /// <summary>Nombre total de groupes détectés, tous types confondus (avant filtrage).</summary>
    public int TotalGroupCount => _allGroups.Count;

    /// <summary>Reconstruit <see cref="Groups"/> à partir de <see cref="_allGroups"/> selon le filtre courant.</summary>
    private void RebuildFilteredView()
    {
        var filter = _selectedMediaFilter?.Type;

        Groups.Clear();
        foreach (var group in _allGroups)
        {
            if (group.Matches(filter))
            {
                Groups.Add(group);
            }
        }

        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(TotalGroupCount));
    }

    /// <summary>Répertoires prioritaires, du plus prioritaire au moins. Ordre = hiérarchie.</summary>
    public ObservableCollection<string> PriorityDirectories { get; } = new();

    /// <summary>
    /// Répertoires (ou motifs) à écarter par défaut : quand un même fichier existe ailleurs, la copie
    /// située sous l'un d'eux est cochée pour suppression. L'ordre est sans importance. Accepte les
    /// motifs génériques (<c>C:\…\Downloads\*</c>, <c>C:\Users\*\Downloads</c>).
    /// </summary>
    public ObservableCollection<string> ExcludedDirectories { get; } = new();

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

    /// <summary>Ajoute un répertoire (ou motif) à écarter par défaut et ré-applique la sélection.</summary>
    public void AddExcludedDirectory(string directory)
    {
        var normalized = NormalizeDirectory(directory);
        if (normalized.Length == 0 ||
            ExcludedDirectories.Any(d => string.Equals(d, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        ExcludedDirectories.Add(normalized);
        ApplyPrioritySelectionToGroups();
        PersistPrioritySettings();
    }

    public void RemoveExcludedDirectory(string directory)
    {
        if (ExcludedDirectories.Remove(directory))
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
                OnPropertyChanged(nameof(CanRunScan));
                OnPropertyChanged(nameof(IsBusy));
            }
        }
    }

    /// <summary>Vrai pendant l'envoi des fichiers à la corbeille (suppression en cours).</summary>
    public bool IsDeleting
    {
        get => _isDeleting;
        private set
        {
            if (SetProperty(ref _isDeleting, value))
            {
                OnPropertyChanged(nameof(CanScan));
                OnPropertyChanged(nameof(CanRunScan));
                OnPropertyChanged(nameof(IsBusy));
            }
        }
    }

    /// <summary>Vrai dès qu'une opération longue est en cours (scan ou suppression) : pilote l'indicateur d'activité.</summary>
    public bool IsBusy => _isScanning || _isDeleting;

    /// <summary>Dossier analysé en dernier (affiché à l'écran).</summary>
    public string CurrentFolder
    {
        get => _currentFolder;
        private set => SetProperty(ref _currentFolder, value);
    }

    /// <summary>Les actions principales sont actives tant qu'aucune opération longue n'est en cours.</summary>
    public bool CanScan => !_isScanning && !_isDeleting;

    /// <summary>Le bouton d'annulation est actif uniquement pendant un scan.</summary>
    public bool CanCancel => _isScanning;

    /// <summary>Détecter les doublons exacts (même nom canonique + taille). Option par défaut.</summary>
    public bool DetectExact
    {
        get => _detectExact;
        set
        {
            if (SetProperty(ref _detectExact, value))
            {
                OnPropertyChanged(nameof(CanRunScan));
            }
        }
    }

    /// <summary>Détecter les doublons flous (noms proches), à confirmer ensuite par hash.</summary>
    public bool DetectFuzzy
    {
        get => _detectFuzzy;
        set
        {
            if (SetProperty(ref _detectFuzzy, value))
            {
                OnPropertyChanged(nameof(CanRunScan));
            }
        }
    }

    /// <summary>Détecter les doublons perceptuels (images recompressées / redimensionnées).</summary>
    public bool DetectPerceptual
    {
        get => _detectPerceptual;
        set
        {
            if (SetProperty(ref _detectPerceptual, value))
            {
                OnPropertyChanged(nameof(CanRunScan));
            }
        }
    }

    /// <summary>Au moins une option de détection est sélectionnée.</summary>
    private bool HasAnyDetectionOption => _detectExact || _detectFuzzy || _detectPerceptual;

    /// <summary>Le bouton de scan est actif si un dossier est choisi, une option cochée, et rien n'est en cours.</summary>
    public bool CanRunScan =>
        !_isScanning && !_isDeleting && _pendingFolderPath is not null && HasAnyDetectionOption;

    /// <summary>Libellé du bouton de scan : « Scanner » avant le premier passage, « Re-scanner » ensuite.</summary>
    public string ScanButtonLabel => _pendingScanned ? "Re-scanner" : "Scanner";

    /// <summary>Mémorise le dossier choisi sans le scanner, et réinitialise l'état « déjà scanné ».</summary>
    public void SelectFolder(string folderPath)
    {
        _pendingFolderPath = folderPath;
        _pendingScanned = false;
        CurrentFolder = folderPath;
        OnPropertyChanged(nameof(CanRunScan));
        OnPropertyChanged(nameof(ScanButtonLabel));
    }

    /// <summary>(Re-)scanne le dossier choisi avec les options de détection cochées.</summary>
    public Task RunScanAsync()
        => _pendingFolderPath is { } folder ? ScanAndDetectAsync(folder) : Task.CompletedTask;

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
        if (IsScanning || IsDeleting || selected.Count == 0)
        {
            return;
        }

        var toDelete = selected.Select(m => new FileToDelete(m.Id, m.Path)).ToList();

        IsDeleting = true;
        StatusText = $"Suppression en cours… {toDelete.Count} fichier(s) vers la corbeille.";

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
        finally
        {
            IsDeleting = false;
        }
    }

    private void ApplyDeletionToGroups(ISet<string> deletedMemberIds)
    {
        for (var i = _allGroups.Count - 1; i >= 0; i--)
        {
            var group = _allGroups[i];
            if (!group.Members.Any(m => deletedMemberIds.Contains(m.Id.Value)))
            {
                continue;
            }

            var updated = group.WithoutMembers(deletedMemberIds);
            if (updated is null)
            {
                _allGroups.RemoveAt(i);
            }
            else
            {
                _allGroups[i] = updated;
            }
        }

        RebuildFilteredView();
    }

    private string RemainingSummary()
    {
        var total = _allGroups.Sum(g => g.ReclaimableBytes);
        return _allGroups.Count == 0
            ? "Plus aucun doublon."
            : $"{_allGroups.Count} groupe(s) restant(s) — {SizeFormatter.Humanize(total)} récupérables.";
    }

    /// <summary>Annule la dernière suppression (restaure depuis la corbeille) et re-détecte.</summary>
    public async Task UndoLastDeleteAsync()
    {
        if (IsScanning || IsDeleting)
        {
            return;
        }

        IsScanning = true;
        StatusText = "Restauration depuis la corbeille…";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            var result = await Task.Run(() => _undoHandler.ExecuteAsync(), token);
            if (!result.HadBatch)
            {
                StatusText = "Rien à annuler.";
                return;
            }

            var prefix = result.Failed == 0
                ? $"{result.Restored} fichier(s) restauré(s). "
                : $"{result.Restored} restauré(s), {result.Failed} échec(s). ";
            await DetectSelectedAsync(prefix, cancellationToken: token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Restauration annulée.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'annulation");
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
        if (IsScanning || IsDeleting)
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
            var excluded = ExcludedDirectories.ToList();
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
                    var keepIndex = PrioritySelection.ChooseKeepIndex(candidates, prefixes, _preferDeeper, excluded);
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

    /// <summary>Vide le catalogue du volume actif.</summary>
    public async Task ClearCatalogAsync()
    {
        if (IsScanning || IsDeleting)
        {
            return;
        }

        try
        {
            await _clearHandler.ExecuteAsync(_activeVolume.Current);
            _allGroups.Clear();
            RebuildFilteredView();
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
        if (IsScanning || IsDeleting)
        {
            return;
        }

        IsScanning = true;
        _allGroups.Clear();
        RebuildFilteredView();
        StatusText = "Analyse en cours…";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            var root = FilePath.From(folderPath);
            _lastRoot = root;
            _pendingFolderPath = root.Value;
            CurrentFolder = root.Value;

            // Inc 9 : identifie/enrôle le volume du dossier choisi et le rend actif. Les instances
            // « default » de ce sous-arbre y sont migrées. Toutes les opérations suivantes (scan,
            // détection, enrichissement) portent sur ce volume.
            StatusText = "Identification du volume…";
            var enroll = await Task.Run(() => _enrollHandler.ExecuteAsync(root, cancellationToken: token), token);
            _activeVolume.Set(enroll.Volume.Id, enroll.Volume.Name);
            if (enroll.MigratedInstances > 0)
            {
                _logger.LogInformation(
                    "{Count} instance(s) migrée(s) du volume « default » vers {Volume}",
                    enroll.MigratedInstances, enroll.Volume.Name);
            }

            // Progress créé sur le thread UI : ses rappels sont marshalés vers l'UI,
            // ce qui permet de lancer le travail lourd sur un thread de fond.
            var progress = new Progress<ScanProgress>(
                p => StatusText = $"Indexation : {p.FilesProcessed} fichiers…");

            // Re-scan incrémental : USN Journal si disponible (NTFS, position connue), sinon
            // parcours mtime complet. Détecte aussi les fichiers disparus (marqués « manquants »).
            var rescan = await Task.Run(() => _rescanHandler.ExecuteAsync(root, enroll.Volume, progress, token), token);
            _logger.LogInformation(
                "Re-scan {Mode} : {Processed} indexé(s), {Missing} manquant(s).",
                rescan.UsedUsnJournal ? "USN" : "mtime", rescan.Processed, rescan.Missing);

            // Enrichissement automatique post-scan, si activé (le handler respecte lui-même le
            // mode en ligne global/par-catégorie et le cache ; rien ne sort en mode local-only).
            await MaybeEnrichAfterScanAsync(root, token);

            await DetectSelectedAsync(cancellationToken: token);

            _pendingScanned = true;
            OnPropertyChanged(nameof(ScanButtonLabel));

            // Rafraîchit la liste des volumes source et y sélectionne le volume venant d'être scanné.
            await LoadVolumesAsync();
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
    /// Lance l'enrichissement en ligne du dossier scanné si le réglage <c>enrich_during_scan</c>
    /// est actif. Best-effort : un échec d'enrichissement ne fait pas échouer le scan.
    /// </summary>
    private async Task MaybeEnrichAfterScanAsync(FilePath root, CancellationToken cancellationToken)
    {
        var raw = await _settings.GetAsync("enrich_during_scan", cancellationToken);
        if (!string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            StatusText = "Enrichissement en ligne…";
            var progress = new Progress<int>(n => StatusText = $"Enrichissement : {n} fichier(s)…");
            var result = await Task.Run(
                () => _enrichHandler.ExecuteAsync(_activeVolume.Current, root, progress, cancellationToken), cancellationToken);
            _logger.LogInformation(
                "Enrichissement post-scan : {Applied} champ(s) appliqué(s) sur {Enriched} fichier(s).",
                result.AppliedFields, result.EnrichedFiles);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Enrichissement post-scan ignoré (erreur non bloquante).");
        }
    }

    /// <summary>
    /// Détecte les doublons selon les options cochées (exacts / flous / perceptuels) et la portée
    /// courante, reconstruit entièrement la liste des groupes, trie par espace récupérable décroissant
    /// et met à jour le statut (éventuellement préfixé par un message d'action, ex. restauration).
    /// </summary>
    private async Task DetectSelectedAsync(
        string? prefix = null, bool wholeCatalog = false, CancellationToken cancellationToken = default)
    {
        // wholeCatalog force le périmètre « tout le volume » (ex. sélection d'un volume source,
        // où _lastRoot — issu d'un autre volume/scan — ne s'applique pas).
        var underRoot = (wholeCatalog || DetectWholeCatalog) ? null : _lastRoot;
        var detected = new List<DuplicateGroupItem>();
        var parts = new List<string>();

        if (_detectExact)
        {
            StatusText = "Détection des doublons exacts…";
            var views = await Task.Run(
                () => _detectHandler.ExecuteDetailedAsync(_activeVolume.Current, underRoot, cancellationToken), cancellationToken);
            var items = views.Select(v => new DuplicateGroupItem(v)).ToList();
            detected.AddRange(items);
            parts.Add($"{items.Count} exact(s)");
        }

        if (_detectFuzzy)
        {
            StatusText = "Détection des doublons flous…";
            var views = await Task.Run(
                () => _fuzzyHandler.ExecuteAsync(_activeVolume.Current, underRoot, _fuzzyThreshold, cancellationToken), cancellationToken);
            var items = views.Select(v => new DuplicateGroupItem(v)).ToList();
            detected.AddRange(items);
            parts.Add($"{items.Count} flou(s)");
        }

        if (_detectPerceptual)
        {
            var progress = new Progress<PerceptualHashProgress>(
                p => StatusText = $"Empreintes perceptuelles : {p.Hashed} image(s) analysée(s)…");
            await Task.Run(
                () => _computePerceptualHandler.ExecuteAsync(_activeVolume.Current, underRoot, progress, cancellationToken), cancellationToken);

            StatusText = "Détection des doublons perceptuels…";
            var views = await Task.Run(
                () => _detectPerceptualHandler.ExecuteAsync(
                    _activeVolume.Current, underRoot, DetectPerceptualDuplicatesHandler.DefaultMaxDistance, cancellationToken),
                cancellationToken);
            var items = views.Select(v => new DuplicateGroupItem(v)).ToList();
            detected.AddRange(items);
            parts.Add($"{items.Count} perceptuel(s)");
        }

        _allGroups.Clear();
        _allGroups.AddRange(detected.OrderByDescending(g => g.ReclaimableBytes));

        RefreshSuggestions();
        ApplyPrioritySelectionToGroups();
        RebuildFilteredView();

        var totalReclaimable = _allGroups.Sum(g => g.ReclaimableBytes);
        var summary = _allGroups.Count == 0
            ? "Aucun doublon trouvé."
            : $"{string.Join(", ", parts)} — {_allGroups.Count} groupe(s), {SizeFormatter.Humanize(totalReclaimable)} récupérables.";
        StatusText = (prefix ?? string.Empty) + summary;
    }

    /// <summary>Recalcule les répertoires suggérés (récurrents, hors priorités déjà choisies).</summary>
    private void RefreshSuggestions()
    {
        var groupDirectorySets = _allGroups
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
    private void ApplyPrioritySelectionToGroups() => ApplyPrioritySelectionTo(_allGroups);

    /// <summary>Applique la sélection par défaut (priorité + score) à un sous-ensemble de groupes.</summary>
    private void ApplyPrioritySelectionTo(IEnumerable<DuplicateGroupItem> groups)
    {
        var prefixes = PriorityDirectories.ToList();
        var excluded = ExcludedDirectories.ToList();

        foreach (var group in groups)
        {
            var candidates = group.Members
                .Select(m => new KeepCandidate(m.Directory, m.Size, System.IO.Path.GetFileName(m.DisplayPath)))
                .ToList();
            var keepIndex = PrioritySelection.ChooseKeepIndex(candidates, prefixes, _preferDeeper, excluded);

            for (var i = 0; i < group.Members.Count; i++)
            {
                group.Members[i].ShouldDelete = i != keepIndex;
            }
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
