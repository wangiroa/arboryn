using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Arboryn.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// ViewModel de la fenêtre d'inventaire (Inc 3) : charge les métriques globales du
/// catalogue et un résumé par LogicalFile (signature, nb de copies, espace récupérable),
/// filtrable par catégorie, volume, répertoire et recherche libre (nom / chemin /
/// métadonnées). Le panneau de droite affiche les métadonnées fusionnées du LogicalFile
/// sélectionné.
/// </summary>
public sealed class InventoryViewModel : INotifyPropertyChanged
{
    /// <summary>Délai d'inactivité avant de relancer la requête sur frappe au clavier.</summary>
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    private readonly ILogicalFileRepository _logicalFiles;
    private readonly IFileMetadataRepository _metadata;
    private readonly ActiveVolumeContext _activeVolume;
    private readonly ILogger<InventoryViewModel> _logger;
    private string _metricsLabel = "Chargement…";
    private string _resultsLabel = string.Empty;
    private string _selectionLabel = "Sélectionne un fichier pour voir ses métadonnées.";
    private string _emptyStateMessage = string.Empty;
    private Visibility _emptyStateVisibility = Visibility.Collapsed;
    private Visibility _listVisibility = Visibility.Visible;
    private LogicalFileItemView? _selectedItem;

    private long _totalLogicalFiles;
    private bool _optionsLoaded;
    private CancellationTokenSource? _searchCts;

    private CategoryFilterOption _selectedCategory = CategoryFilterOption.All;
    private VolumeFilterOption _selectedVolume = VolumeFilterOption.All;
    private string _directoryFilter = string.Empty;
    private string _searchText = string.Empty;

    public InventoryViewModel(
        ILogicalFileRepository logicalFiles,
        IFileMetadataRepository metadata,
        ActiveVolumeContext activeVolume,
        ILogger<InventoryViewModel> logger)
    {
        _logicalFiles = logicalFiles;
        _metadata = metadata;
        _activeVolume = activeVolume;
        _logger = logger;

        Categories.Add(CategoryFilterOption.All);
        Volumes.Add(VolumeFilterOption.All);
    }

    public ObservableCollection<LogicalFileItemView> Items { get; } = new();

    public ObservableCollection<MetadataItemView> SelectedMetadata { get; } = new();

    public ObservableCollection<CategoryFilterOption> Categories { get; } = new();

    public ObservableCollection<VolumeFilterOption> Volumes { get; } = new();

    public string MetricsLabel
    {
        get => _metricsLabel;
        private set => SetField(ref _metricsLabel, value, nameof(MetricsLabel));
    }

    public string ResultsLabel
    {
        get => _resultsLabel;
        private set => SetField(ref _resultsLabel, value, nameof(ResultsLabel));
    }

    public string EmptyStateMessage
    {
        get => _emptyStateMessage;
        private set => SetField(ref _emptyStateMessage, value, nameof(EmptyStateMessage));
    }

    public Visibility EmptyStateVisibility
    {
        get => _emptyStateVisibility;
        private set
        {
            if (_emptyStateVisibility != value)
            {
                _emptyStateVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EmptyStateVisibility)));
            }
        }
    }

    public Visibility ListVisibility
    {
        get => _listVisibility;
        private set
        {
            if (_listVisibility != value)
            {
                _listVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ListVisibility)));
            }
        }
    }

    /// <summary>Dossier de filtrage choisi via le sélecteur (vide = aucun filtre répertoire).</summary>
    public string DirectoryFilter
    {
        get => _directoryFilter;
        private set
        {
            if (_directoryFilter != value)
            {
                _directoryFilter = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DirectoryFilter)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DirectoryFilterDisplay)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDirectoryFilter)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DirectoryClearVisibility)));
            }
        }
    }

    /// <summary>Libellé affiché : le dossier choisi, ou un texte d'invite.</summary>
    public string DirectoryFilterDisplay =>
        string.IsNullOrEmpty(_directoryFilter) ? "Tous les répertoires" : _directoryFilter;

    public bool HasDirectoryFilter => !string.IsNullOrEmpty(_directoryFilter);

    public Visibility DirectoryClearVisibility =>
        HasDirectoryFilter ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Applique un dossier choisi par le sélecteur et recharge.</summary>
    public void SetDirectoryFilter(string? path)
    {
        var resolved = string.IsNullOrWhiteSpace(path) ? string.Empty : path!.TrimEnd('\\', '/');
        if (_directoryFilter != resolved)
        {
            DirectoryFilter = resolved;
            _ = ReloadAsync();
        }
    }

    /// <summary>Efface le filtre par répertoire.</summary>
    public void ClearDirectoryFilter() => SetDirectoryFilter(null);

    public CategoryFilterOption SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (value is not null && !ReferenceEquals(_selectedCategory, value))
            {
                _selectedCategory = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCategory)));
                _ = ReloadAsync();
            }
        }
    }

    public VolumeFilterOption SelectedVolume
    {
        get => _selectedVolume;
        set
        {
            if (value is not null && !ReferenceEquals(_selectedVolume, value))
            {
                _selectedVolume = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedVolume)));
                _ = ReloadAsync();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value ?? string.Empty;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchText)));
                _ = ReloadDebouncedAsync();
            }
        }
    }

    public string SelectionLabel
    {
        get => _selectionLabel;
        private set => SetField(ref _selectionLabel, value, nameof(SelectionLabel));
    }

    public LogicalFileItemView? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!ReferenceEquals(_selectedItem, value))
            {
                _selectedItem = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedItem)));
                _ = OnSelectionChangedAsync();
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var (metrics, options) = await Task.Run(async () =>
            {
                var m = await _logicalFiles.GetMetricsAsync(_activeVolume.Current, cancellationToken).ConfigureAwait(false);
                var o = await _logicalFiles.GetFilterOptionsAsync(cancellationToken).ConfigureAwait(false);
                return (Metrics: m, Options: o);
            }, cancellationToken);

            _totalLogicalFiles = metrics.LogicalFiles;
            MetricsLabel = $"{metrics.LogicalFiles:N0} LogicalFiles  —  {metrics.FileInstances:N0} instances  " +
                           $"—  redondance {metrics.RedundancyRatio:0.##}×";

            PopulateOptions(options);
            _optionsLoaded = true;

            await ReloadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Échec du chargement de l'inventaire");
            MetricsLabel = $"Erreur : {ex.Message}";
        }
    }

    private void PopulateOptions(CatalogFilterOptions options)
    {
        Categories.Clear();
        Categories.Add(CategoryFilterOption.All);
        foreach (var category in options.Categories)
        {
            Categories.Add(new CategoryFilterOption(category));
        }
        _selectedCategory = CategoryFilterOption.All;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCategory)));

        Volumes.Clear();
        Volumes.Add(VolumeFilterOption.All);
        foreach (var volume in options.Volumes)
        {
            Volumes.Add(new VolumeFilterOption(volume.Id, volume.Name));
        }
        _selectedVolume = VolumeFilterOption.All;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedVolume)));

        DirectoryFilter = string.Empty;
    }

    /// <summary>Réinitialise tous les filtres et recharge la liste complète.</summary>
    public void ResetFilters()
    {
        _selectedCategory = CategoryFilterOption.All;
        _selectedVolume = VolumeFilterOption.All;
        _searchText = string.Empty;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCategory)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedVolume)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchText)));
        DirectoryFilter = string.Empty;
        _ = ReloadAsync();
    }

    /// <summary>Vrai si au moins un critère de filtrage est actif.</summary>
    private bool HasActiveFilter =>
        _selectedCategory.Value is not null
        || _selectedVolume.Id is not null
        || !string.IsNullOrEmpty(_directoryFilter)
        || !string.IsNullOrWhiteSpace(_searchText);

    private CatalogFilter BuildFilter() => new(
        Category: _selectedCategory.Value,
        VolumeId: _selectedVolume.Id,
        Directory: string.IsNullOrEmpty(_directoryFilter) ? null : _directoryFilter,
        Search: string.IsNullOrWhiteSpace(_searchText) ? null : _searchText);

    private async Task ReloadDebouncedAsync()
    {
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _searchCts, cts)?.Cancel();

        try
        {
            await Task.Delay(SearchDebounce, cts.Token).ConfigureAwait(true);
            await ReloadAsync(cts.Token).ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            // Une frappe plus récente a annulé ce cycle — comportement attendu.
        }
    }

    private async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (!_optionsLoaded)
        {
            return;
        }

        try
        {
            var filter = BuildFilter();
            var summaries = await Task.Run(
                () => _logicalFiles.GetSummariesAsync(filter, cancellationToken), cancellationToken)
                .ConfigureAwait(true);

            Items.Clear();
            foreach (var summary in summaries)
            {
                Items.Add(new LogicalFileItemView(summary));
            }

            ResultsLabel = summaries.Count == _totalLogicalFiles
                ? $"{summaries.Count:N0} résultat(s)"
                : $"{summaries.Count:N0} résultat(s) sur {_totalLogicalFiles:N0}";

            UpdateEmptyState(summaries.Count, hasError: false);
        }
        catch (System.OperationCanceledException)
        {
            // Rechargement remplacé par un plus récent.
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Échec du filtrage de l'inventaire");
            ResultsLabel = $"Erreur : {ex.Message}";
            Items.Clear();
            UpdateEmptyState(0, hasError: true, ex.Message);
        }
    }

    private void UpdateEmptyState(int count, bool hasError, string? error = null)
    {
        if (count > 0)
        {
            EmptyStateVisibility = Visibility.Collapsed;
            ListVisibility = Visibility.Visible;
            return;
        }

        EmptyStateMessage = hasError
            ? $"Impossible de charger le catalogue.\n{error}"
            : _totalLogicalFiles == 0
                ? "Le catalogue est vide. Lance un scan pour le remplir."
                : HasActiveFilter
                    ? "Aucun fichier ne correspond aux filtres. Élargis ou réinitialise la recherche."
                    : "Aucun fichier à afficher.";

        EmptyStateVisibility = Visibility.Visible;
        ListVisibility = Visibility.Collapsed;
    }

    private async Task OnSelectionChangedAsync()
    {
        SelectedMetadata.Clear();

        if (_selectedItem is null)
        {
            SelectionLabel = "Sélectionne un fichier pour voir ses métadonnées.";
            return;
        }

        SelectionLabel = $"Métadonnées de {_selectedItem.SignatureDisplay}";

        try
        {
            var fused = await Task.Run(
                () => _metadata.GetFusedAsync(_selectedItem.SampleInstanceId, CancellationToken.None));

            foreach (var pair in fused.OrderBy(kv => kv.Key, System.StringComparer.Ordinal))
            {
                SelectedMetadata.Add(new MetadataItemView(pair.Value));
            }

            if (SelectedMetadata.Count == 0)
            {
                SelectionLabel += " — aucune métadonnée extraite.";
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Échec du chargement des métadonnées");
            SelectionLabel = $"Erreur : {ex.Message}";
        }
    }

    private void SetField(ref string field, string value, string propertyName)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Projection bindable d'un <see cref="LogicalFileSummary"/>.</summary>
public sealed class LogicalFileItemView
{
    public FileInstanceId SampleInstanceId { get; }
    public string KindBadge { get; }
    public string CategoryBadge { get; }
    public string SignatureDisplay { get; }
    public string Subtitle { get; }
    public string LocationLine { get; }

    public LogicalFileItemView(LogicalFileSummary summary)
    {
        SampleInstanceId = summary.SampleInstanceId;
        KindBadge = summary.Signature.Kind switch
        {
            ContentSignatureKind.NameSize => "Nom",
            ContentSignatureKind.Sha256 => "Hash ✓",
            ContentSignatureKind.PHash => "pHash",
            ContentSignatureKind.Chromaprint => "Chromaprint",
            _ => summary.Signature.Kind.ToString(),
        };

        CategoryBadge = CategoryLabel(summary.Category);

        SignatureDisplay = summary.Signature.Kind == ContentSignatureKind.Sha256
            ? $"#{summary.Signature.Value[..16]}…"
            : summary.Signature.Value;

        Subtitle = $"{summary.InstanceCount} copie(s) — {SizeFormatter.Humanize(summary.TotalSize)} total — " +
                   $"récupérable {SizeFormatter.Humanize(summary.ReclaimableBytes)}";

        var directory = string.IsNullOrEmpty(summary.Directory) ? "—" : summary.Directory;
        LocationLine = $"{summary.VolumeName} · {directory}";
    }

    internal static string CategoryLabel(MediaCategory category) => category switch
    {
        MediaCategory.Audiobook => "Livre audio",
        MediaCategory.Book => "Livre",
        MediaCategory.Video => "Vidéo",
        MediaCategory.Photo => "Photo",
        MediaCategory.OfficialDocument => "Document officiel",
        MediaCategory.OtherDocument => "Document",
        _ => "Non classé",
    };
}

/// <summary>Option du filtre de catégorie (« Toutes » = <see cref="Value"/> nul).</summary>
public sealed class CategoryFilterOption
{
    public static readonly CategoryFilterOption All = new(null, "Toutes les catégories");

    public MediaCategory? Value { get; }
    public string Label { get; }

    public CategoryFilterOption(MediaCategory category)
        : this(category, LogicalFileItemView.CategoryLabel(category))
    {
    }

    private CategoryFilterOption(MediaCategory? value, string label)
    {
        Value = value;
        Label = label;
    }
}

/// <summary>Option du filtre de volume (« Tous » = <see cref="Id"/> nul).</summary>
public sealed class VolumeFilterOption
{
    public static readonly VolumeFilterOption All = new(null, "Tous les volumes");

    public string? Id { get; }
    public string Label { get; }

    public VolumeFilterOption(string? id, string label)
    {
        Id = id;
        Label = label;
    }
}

/// <summary>Projection bindable d'une <see cref="MetadataEntry"/> fusionnée.</summary>
public sealed class MetadataItemView
{
    public string Key { get; }
    public string Value { get; }
    public string Source { get; }
    public string Confidence { get; }

    public MetadataItemView(MetadataEntry entry)
    {
        Key = DisplayKey(entry.Key);
        Value = entry.Value ?? "—";
        Source = entry.Source;
        Confidence = entry.Confidence.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string DisplayKey(string key) => key switch
    {
        "title" => "Titre",
        "subtitle" => "Sous-titre",
        "artist" => "Artiste",
        "album" => "Album",
        "album_artist" => "Artiste album",
        "year" => "Année",
        "date" => "Date",
        "duration_seconds" => "Durée (s)",
        "track_number" => "Piste",
        "total_tracks" => "Pistes total",
        "disc_number" => "Disque",
        "genre" => "Genre",
        "author" => "Auteur",
        "publisher" => "Éditeur",
        "language" => "Langue",
        "isbn" => "ISBN",
        "width" => "Largeur",
        "height" => "Hauteur",
        "date_taken" => "Pris le",
        "camera_make" => "Marque appareil",
        "camera_model" => "Modèle appareil",
        "gps_latitude" => "GPS latitude",
        "gps_longitude" => "GPS longitude",
        "resolution" => "Résolution",
        "codec" => "Codec",
        "release_group" => "Groupe",
        "source_tag" => "Source",
        _ => key,
    };
}
