using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Arboryn.Application.Inventory;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// ViewModel du tableau de bord (Inc 11) : instantané de la bibliothèque — compteurs globaux,
/// santé (volumes en ligne / hors-ligne, scans anciens, opérations en attente), synthèse par
/// catégorie, et matrice volume × catégorie (présent / en-scope, manques et surplus).
/// </summary>
public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private readonly InventoryDashboardHandler _dashboard;
    private readonly ILogger<DashboardViewModel> _logger;

    private bool _isLoading = true;
    private string _statusLine = "Chargement de l'inventaire…";
    private long _logicalFiles;
    private long _fileInstances;
    private double _redundancy;
    private long _totalSpace;
    private int _onlineVolumes;
    private int _totalVolumes;
    private int _offlineVolumes;
    private int _staleVolumes;
    private int _pendingOperations;
    private string _lastScanText = "—";

    public DashboardViewModel(InventoryDashboardHandler dashboard, ILogger<DashboardViewModel> logger)
    {
        _dashboard = dashboard;
        _logger = logger;
    }

    public ObservableCollection<VolumeMatrixRow> Volumes { get; } = new();

    public ObservableCollection<CategoryRow> Categories { get; } = new();

    public bool IsLoading { get => _isLoading; private set => SetField(ref _isLoading, value); }

    public string StatusLine { get => _statusLine; private set => SetField(ref _statusLine, value); }

    public string LogicalFilesText => _logicalFiles.ToString("N0", Fr);

    public string FileInstancesText => _fileInstances.ToString("N0", Fr);

    public string RedundancyText => _logicalFiles == 0 ? "—" : $"{_redundancy.ToString("0.00", Fr)}×";

    public string TotalSpaceText => FormatBytes(_totalSpace);

    public string VolumesText => $"{_onlineVolumes}/{_totalVolumes}";

    public string OfflineText => _offlineVolumes.ToString("N0", Fr);

    public string StaleText => _staleVolumes.ToString("N0", Fr);

    public string PendingOperationsText => _pendingOperations.ToString("N0", Fr);

    public string LastScanText { get => _lastScanText; private set => SetField(ref _lastScanText, value); }

    public bool HasVolumes => Volumes.Count > 0;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        StatusLine = "Chargement de l'inventaire…";
        try
        {
            var snapshot = await Task.Run(() => _dashboard.ExecuteAsync(cancellationToken), cancellationToken).ConfigureAwait(true);

            _logicalFiles = snapshot.Global.LogicalFiles;
            _fileInstances = snapshot.Global.FileInstances;
            _redundancy = snapshot.Global.RedundancyRatio;
            _totalSpace = snapshot.Global.TotalSpaceBytes;
            _totalVolumes = snapshot.Volumes.Count;
            _offlineVolumes = snapshot.Health.OfflineVolumes;
            _onlineVolumes = _totalVolumes - _offlineVolumes;
            _staleVolumes = snapshot.Health.StaleVolumes;
            _pendingOperations = snapshot.Health.PendingOperations;
            LastScanText = snapshot.Health.OldestScan is { } scan
                ? scan.ToLocalTime().ToString("dd/MM/yyyy", Fr)
                : "Jamais";

            Categories.Clear();
            foreach (var category in snapshot.Categories)
            {
                Categories.Add(new CategoryRow(
                    CategoryLabels.Of(category.Category),
                    $"{category.LogicalFiles.ToString("N0", Fr)} œuvres",
                    FormatBytes(category.SpaceBytes)));
            }

            Volumes.Clear();
            foreach (var volume in snapshot.Volumes)
            {
                Volumes.Add(new VolumeMatrixRow(volume));
            }

            RaiseAll();
            StatusLine = _totalVolumes == 0
                ? "Aucun volume enrôlé. Enrôlez vos disques et NAS depuis l'écran « Volumes »."
                : $"{LogicalFilesText} œuvres · {FileInstancesText} copies · {TotalSpaceText} sur {_totalVolumes} volume(s).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du chargement du tableau de bord");
            StatusLine = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RaiseAll()
    {
        foreach (var name in new[]
        {
            nameof(LogicalFilesText), nameof(FileInstancesText), nameof(RedundancyText), nameof(TotalSpaceText),
            nameof(VolumesText), nameof(OfflineText), nameof(StaleText), nameof(PendingOperationsText), nameof(HasVolumes),
        })
        {
            OnPropertyChanged(name);
        }
    }

    internal static string FormatBytes(long bytes)
    {
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size.ToString("0.#", Fr)} {units[unit]}";
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Synthèse d'une catégorie sur l'ensemble du catalogue.</summary>
public sealed class CategoryRow
{
    public CategoryRow(string label, string countText, string spaceText)
    {
        Label = label;
        CountText = countText;
        SpaceText = spaceText;
    }

    public string Label { get; }

    public string CountText { get; }

    public string SpaceText { get; }
}

/// <summary>Ligne de la matrice : un volume, ses totaux et ses cellules par catégorie.</summary>
public sealed class VolumeMatrixRow
{
    public VolumeMatrixRow(VolumeInventory volume)
    {
        Id = volume.Id;
        Name = volume.Name;
        StatusLabel = volume.Status switch
        {
            Arboryn.Domain.Enums.VolumeStatus.Online => "Connecté",
            Arboryn.Domain.Enums.VolumeStatus.Offline => "Hors-ligne",
            _ => "Inconnu",
        };
        ScopeSummary = volume.HasScope
            ? $"{volume.GapCount} manque(s) · {volume.SurplusCount} surplus"
            : "Aucun périmètre défini";
        SpaceText = DashboardViewModel.FormatBytes(volume.SpaceBytes);
        HasGapOrSurplus = volume.HasScope && (volume.GapCount > 0 || volume.SurplusCount > 0);
        Cells = volume.Cells.Select(c => new MatrixCellView(c)).ToList();
    }

    public VolumeId Id { get; }

    public string Name { get; }

    public string StatusLabel { get; }

    public string ScopeSummary { get; }

    public string SpaceText { get; }

    public bool HasGapOrSurplus { get; }

    public IReadOnlyList<MatrixCellView> Cells { get; }
}

/// <summary>Cellule volume × catégorie : « présent/en-scope », teintée selon manque/surplus.</summary>
public sealed class MatrixCellView
{
    public MatrixCellView(VolumeCategoryCell cell)
    {
        Label = CategoryLabels.Of(cell.Category);
        ValueText = cell.InScope > 0 ? $"{cell.Present}/{cell.InScope}" : cell.Present.ToString(CultureInfo.InvariantCulture);

        // Teinte : rouge si manque, ambre si surplus, neutre sinon.
        var (bg, fg) = cell.Gap > 0
            ? ("ArborynDangerBgBrush", "ArborynDangerBrush")
            : cell.Surplus > 0
                ? ("ArborynCautionBgBrush", "ArborynCautionBrush")
                : ("ArborynCardSecondaryBrush", "ArborynTextSecondaryBrush");
        Background = Brush(bg);
        Foreground = Brush(fg);
    }

    public string Label { get; }

    public string ValueText { get; }

    public Brush Background { get; }

    public Brush Foreground { get; }

    private static Brush Brush(string key) => (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];
}
