using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// ViewModel de la fenêtre d'inventaire (Inc 3) : charge les métriques globales du
/// catalogue et un résumé par LogicalFile (signature, nb de copies, espace récupérable).
/// Le panneau de droite affiche les métadonnées fusionnées du LogicalFile sélectionné.
/// </summary>
public sealed class InventoryViewModel : INotifyPropertyChanged
{
    private readonly ILogicalFileRepository _logicalFiles;
    private readonly IFileMetadataRepository _metadata;
    private readonly ILogger<InventoryViewModel> _logger;
    private string _metricsLabel = "Chargement…";
    private string _selectionLabel = "Sélectionne un fichier pour voir ses métadonnées.";
    private LogicalFileItemView? _selectedItem;

    public InventoryViewModel(
        ILogicalFileRepository logicalFiles,
        IFileMetadataRepository metadata,
        ILogger<InventoryViewModel> logger)
    {
        _logicalFiles = logicalFiles;
        _metadata = metadata;
        _logger = logger;
    }

    public ObservableCollection<LogicalFileItemView> Items { get; } = new();

    public ObservableCollection<MetadataItemView> SelectedMetadata { get; } = new();

    public string MetricsLabel
    {
        get => _metricsLabel;
        private set => SetField(ref _metricsLabel, value, nameof(MetricsLabel));
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
            var (metrics, summaries) = await Task.Run(async () =>
            {
                var m = await _logicalFiles.GetMetricsAsync(VolumeId.Default, cancellationToken).ConfigureAwait(false);
                var s = await _logicalFiles.GetSummariesAsync(VolumeId.Default, cancellationToken).ConfigureAwait(false);
                return (Metrics: m, Summaries: s);
            }, cancellationToken);

            MetricsLabel = $"{metrics.LogicalFiles:N0} LogicalFiles  —  {metrics.FileInstances:N0} instances  " +
                           $"—  redondance {metrics.RedundancyRatio:0.##}×";

            Items.Clear();
            foreach (var summary in summaries)
            {
                Items.Add(new LogicalFileItemView(summary));
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Échec du chargement de l'inventaire");
            MetricsLabel = $"Erreur : {ex.Message}";
        }
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
    public string SignatureDisplay { get; }
    public string Subtitle { get; }

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

        SignatureDisplay = summary.Signature.Kind == ContentSignatureKind.Sha256
            ? $"#{summary.Signature.Value[..16]}…"
            : summary.Signature.Value;

        Subtitle = $"{summary.InstanceCount} copie(s) — {SizeFormatter.Humanize(summary.TotalSize)} total — " +
                   $"récupérable {SizeFormatter.Humanize(summary.ReclaimableBytes)}";
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
