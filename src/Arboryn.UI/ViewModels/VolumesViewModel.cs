using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
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
/// ViewModel de la page Volumes (Inc 9) : liste les volumes connus (avec statut, support,
/// identité, dernier scan, taille), permet d'enrôler le volume d'un dossier choisi et de
/// choisir le volume actif sur lequel portent les opérations (scan, doublons, uniformisation…).
/// </summary>
public sealed class VolumesViewModel : INotifyPropertyChanged
{
    private readonly IVolumeRepository _volumes;
    private readonly IMachineRepository _machines;
    private readonly EnrollVolumeHandler _enroll;
    private readonly ActiveVolumeContext _activeVolume;
    private readonly ILogger<VolumesViewModel> _logger;

    private bool _isBusy;
    private string _statusText = "Enrôlez un dossier pour identifier son volume, ou choisissez le volume actif.";

    public VolumesViewModel(
        IVolumeRepository volumes,
        IMachineRepository machines,
        EnrollVolumeHandler enroll,
        ActiveVolumeContext activeVolume,
        ILogger<VolumesViewModel> logger)
    {
        _volumes = volumes;
        _machines = machines;
        _enroll = enroll;
        _activeVolume = activeVolume;
        _logger = logger;
        _activeVolume.Changed += (_, _) => RefreshActiveFlags();
    }

    public ObservableCollection<VolumeRowItem> Volumes { get; } = new();

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

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public string ActiveVolumeText => $"Volume actif : {_activeVolume.CurrentName}";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var records = await _volumes.GetAllAsync(cancellationToken).ConfigureAwait(true);
            var machines = await _machines.GetAllAsync(cancellationToken).ConfigureAwait(true);
            var machineNames = machines.ToDictionary(m => m.Id.Value, m => m.Name, StringComparer.Ordinal);
            Volumes.Clear();
            foreach (var record in records)
            {
                var machineName = record.MachineId is { } mid ? machineNames.GetValueOrDefault(mid) : null;
                Volumes.Add(new VolumeRowItem(record, record.Id == _activeVolume.Current, machineName));
            }

            OnPropertyChanged(nameof(ActiveVolumeText));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du chargement des volumes");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Enrôle (ou reconnaît) le volume hébergeant le dossier choisi et le rend actif.</summary>
    public async Task EnrollFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Identification du volume…";
        try
        {
            var root = FilePath.From(folderPath);
            var result = await Task.Run(() => _enroll.ExecuteAsync(root, cancellationToken: cancellationToken), cancellationToken);
            _activeVolume.Set(result.Volume.Id, result.Volume.Name);

            var action = result.IsNewlyEnrolled ? "enrôlé" : "reconnu";
            var migrated = result.MigratedInstances > 0
                ? $" — {result.MigratedInstances} instance(s) migrée(s) depuis « default »"
                : string.Empty;
            var marker = result.MarkerWritten ? string.Empty : " (marqueur non écrit : volume en lecture seule)";
            StatusText = $"Volume « {result.Volume.Name} » {action}{migrated}{marker}.";

            await LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'enrôlement du dossier {Folder}", folderPath);
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Définit le volume actif (les opérations suivantes porteront sur lui).</summary>
    public void SetActive(VolumeRowItem item)
    {
        _activeVolume.Set(item.Id, item.Name);
        StatusText = $"Volume actif : « {item.Name} ».";
    }

    /// <summary>
    /// Renomme un volume (nom convivial). La base fait foi pour un volume enrôlé ; le marqueur
    /// <c>.Arboryn</c> n'est mis à jour qu'au prochain enrôlement, donc le renommage fonctionne
    /// aussi hors-ligne. Si le volume renommé est actif, son libellé est synchronisé.
    /// </summary>
    public async Task RenameAsync(VolumeRowItem item, string newName, CancellationToken cancellationToken = default)
    {
        var trimmed = newName?.Trim();
        if (IsBusy || string.IsNullOrWhiteSpace(trimmed) || string.Equals(trimmed, item.Name, StringComparison.Ordinal))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var record = await _volumes.GetAsync(item.Id, cancellationToken).ConfigureAwait(true);
            if (record is null)
            {
                StatusText = "Volume introuvable.";
                return;
            }

            await _volumes.UpsertAsync(record with { Name = trimmed }, cancellationToken).ConfigureAwait(true);
            if (item.Id == _activeVolume.Current)
            {
                _activeVolume.Set(item.Id, trimmed);
            }

            StatusText = $"Volume renommé en « {trimmed} ».";
            await LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du renommage du volume {Volume}", item.Id.Value);
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshActiveFlags()
    {
        foreach (var row in Volumes)
        {
            row.IsActive = row.Id == _activeVolume.Current;
        }

        OnPropertyChanged(nameof(ActiveVolumeText));
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

/// <summary>Ligne d'affichage d'un volume dans la page Volumes.</summary>
public sealed class VolumeRowItem : INotifyPropertyChanged
{
    private bool _isActive;

    public VolumeRowItem(VolumeRecord record, bool isActive, string? machineName = null)
    {
        Id = record.Id;
        Name = record.Name;
        KindLabel = KindToLabel(record.Kind);
        StatusLabel = StatusToLabel(record.Status);
        Identity = BuildIdentity(record);
        MachineLabel = BuildMachineLabel(record.Kind, machineName);
        MountPoint = string.IsNullOrWhiteSpace(record.MountPoint) ? "—" : record.MountPoint!;
        LastScanText = record.LastScanAt is { } scan
            ? scan.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("fr-FR"))
            : "Jamais scanné";
        SizeText = TryDriveSize(record);
        _isActive = isActive;
    }

    public VolumeId Id { get; }

    public string Name { get; }

    public string KindLabel { get; }

    public string StatusLabel { get; }

    public string Identity { get; }

    /// <summary>Machine (PC) propriétaire, pour distinguer deux volumes homonymes (Inc 13).</summary>
    public string MachineLabel { get; }

    public string MountPoint { get; }

    public string LastScanText { get; }

    public string SizeText { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
            }
        }
    }

    private static string KindToLabel(VolumeKind kind) => kind switch
    {
        VolumeKind.Internal => "Interne",
        VolumeKind.External => "Externe (USB)",
        VolumeKind.Nas => "NAS / réseau",
        VolumeKind.Default => "Défaut (pré-multi-volume)",
        _ => "Autre",
    };

    private static string StatusToLabel(VolumeStatus status) => status switch
    {
        VolumeStatus.Online => "Connecté",
        VolumeStatus.Offline => "Hors-ligne",
        _ => "Inconnu",
    };

    private static string BuildMachineLabel(VolumeKind kind, string? machineName) => kind switch
    {
        VolumeKind.Nas => "NAS partagé",
        VolumeKind.Default => "—",
        _ => string.IsNullOrWhiteSpace(machineName) ? "Machine inconnue" : machineName!,
    };

    private static string BuildIdentity(VolumeRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.Serial))
        {
            return $"VSN {record.Serial}";
        }

        if (!string.IsNullOrWhiteSpace(record.Fingerprint))
        {
            return record.Fingerprint!;
        }

        return "—";
    }

    /// <summary>Taille du support si lisible (best-effort, lecteurs locaux connectés uniquement).</summary>
    private static string TryDriveSize(VolumeRecord record)
    {
        var mount = record.MountPoint;
        if (string.IsNullOrWhiteSpace(mount) || mount.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return "—";
        }

        try
        {
            var drive = new DriveInfo(mount);
            if (!drive.IsReady)
            {
                return "—";
            }

            var used = drive.TotalSize - drive.AvailableFreeSpace;
            return $"{FormatBytes(used)} / {FormatBytes(drive.TotalSize)}";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return "—";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size.ToString("0.#", CultureInfo.GetCultureInfo("fr-FR"))} {units[unit]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
