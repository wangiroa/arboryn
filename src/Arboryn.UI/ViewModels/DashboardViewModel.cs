using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// ViewModel du tableau de bord : agrège les métriques globales du catalogue
/// pour la carte « Inventaire » et la carte « Santé ».
/// </summary>
public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private readonly ILogicalFileRepository _logicalFiles;
    private readonly ILogger<DashboardViewModel> _logger;

    private long _logicalFileCount;
    private long _fileInstanceCount;
    private double _redundancyRatio;
    private bool _isLoading = true;
    private string _statusLine = "Chargement de l'inventaire…";

    public DashboardViewModel(ILogicalFileRepository logicalFiles, ILogger<DashboardViewModel> logger)
    {
        _logicalFiles = logicalFiles;
        _logger = logger;
    }

    public long LogicalFileCount
    {
        get => _logicalFileCount;
        private set => SetField(ref _logicalFileCount, value);
    }

    public long FileInstanceCount
    {
        get => _fileInstanceCount;
        private set => SetField(ref _fileInstanceCount, value);
    }

    public double RedundancyRatio
    {
        get => _redundancyRatio;
        private set => SetField(ref _redundancyRatio, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public string StatusLine
    {
        get => _statusLine;
        private set => SetField(ref _statusLine, value);
    }

    public string LogicalFileCountText => LogicalFileCount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));

    public string FileInstanceCountText => FileInstanceCount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));

    public string RedundancyText
    {
        get
        {
            if (LogicalFileCount == 0)
            {
                return "—";
            }
            return $"{RedundancyRatio:0.00}× ({(RedundancyRatio - 1d) * 100d:+0;-0;0} %)";
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        StatusLine = "Chargement de l'inventaire…";

        try
        {
            var metrics = await Task.Run(
                () => _logicalFiles.GetMetricsAsync(VolumeId.Default, cancellationToken),
                cancellationToken).ConfigureAwait(true);

            LogicalFileCount = metrics.LogicalFiles;
            FileInstanceCount = metrics.FileInstances;
            RedundancyRatio = metrics.RedundancyRatio;
            OnPropertyChanged(nameof(LogicalFileCountText));
            OnPropertyChanged(nameof(FileInstanceCountText));
            OnPropertyChanged(nameof(RedundancyText));

            StatusLine = LogicalFileCount == 0
                ? "Aucun fichier indexé pour le moment. Ouvre la page Doublons pour lancer un premier scan."
                : $"{LogicalFileCountText} œuvres uniques · {FileInstanceCountText} copies physiques.";
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Échec du chargement du tableau de bord");
            StatusLine = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return;
        }
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged(string? name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;
}
