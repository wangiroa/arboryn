using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Arboryn.Application.Abstractions;
using Arboryn.Infrastructure.Database;
using Microsoft.Extensions.Logging;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// ViewModel de la carte « Emplacement de la base » des Réglages (Inc 13, A2). Affiche le
/// chemin effectif, choisit un emplacement partageable (clé USB / dossier partagé), et pilote
/// l'export/import sûr du catalogue. Choix d'emplacement et import prennent effet au redémarrage.
/// </summary>
public sealed class DatabaseSettingsViewModel : INotifyPropertyChanged
{
    private readonly DatabaseLocationInfo _location;
    private readonly ICatalogTransfer _transfer;
    private readonly ILogger<DatabaseSettingsViewModel> _logger;

    private string _statusText = string.Empty;

    public DatabaseSettingsViewModel(
        DatabaseLocationInfo location,
        ICatalogTransfer transfer,
        ILogger<DatabaseSettingsViewModel> logger)
    {
        _location = location;
        _transfer = transfer;
        _logger = logger;
    }

    /// <summary>Chemin effectif de la base pour cette session.</summary>
    public string DatabasePath => _location.DatabasePath;

    /// <summary>
    /// <c>true</c> si la base est sur un emplacement risqué (réseau / amovible / dossier
    /// synchronisé) : ouvrir SQLite en direct y est dangereux → on prévient l'utilisateur.
    /// </summary>
    public bool IsRiskyLocation => LooksRisky(_location.DatabasePath);

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    /// <summary>Définit un nouvel emplacement (dossier) ; la base y sera <c>index.db</c> au redémarrage.</summary>
    public void ChooseLocation(string folderPath)
    {
        try
        {
            var target = Path.Combine(folderPath, DatabaseLocation.DefaultDbFileName);
            DatabaseLocation.WritePointer(_location.ArborynDir, target);
            StatusText = $"Nouvel emplacement : {target}. Redémarrez Arboryn pour l'appliquer.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Échec de l'écriture du pointeur d'emplacement de base");
            StatusText = $"Erreur : {ex.Message}";
        }
    }

    /// <summary>Revient à l'emplacement par défaut (LOCALAPPDATA) au prochain démarrage.</summary>
    public void ResetToDefault()
    {
        try
        {
            DatabaseLocation.ClearPointer(_location.ArborynDir);
            StatusText = "Emplacement réinitialisé (local). Redémarrez Arboryn pour l'appliquer.";
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Échec de la réinitialisation du pointeur d'emplacement de base");
            StatusText = $"Erreur : {ex.Message}";
        }
    }

    /// <summary>Exporte une copie cohérente de la base vers <paramref name="destinationPath"/>.</summary>
    public async Task ExportAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        try
        {
            await _transfer.ExportAsync(destinationPath, cancellationToken).ConfigureAwait(true);
            StatusText = $"Base exportée vers {destinationPath}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'export de la base vers {Destination}", destinationPath);
            StatusText = $"Erreur d'export : {ex.Message}";
        }
    }

    /// <summary>Planifie le remplacement de la base par <paramref name="sourcePath"/> au redémarrage.</summary>
    public void ScheduleImport(string sourcePath)
    {
        try
        {
            _transfer.ScheduleImport(sourcePath);
            StatusText = $"Import planifié depuis {sourcePath}. Redémarrez Arboryn pour l'appliquer.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Échec de la planification de l'import depuis {Source}", sourcePath);
            StatusText = $"Erreur : {ex.Message}";
        }
    }

    private static bool LooksRisky(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;   // UNC / partage réseau
        }

        if (path.Contains("OneDrive", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Dropbox", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Google Drive", StringComparison.OrdinalIgnoreCase))
        {
            return true;   // dossier synchronisé
        }

        try
        {
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (drive.DriveType is DriveType.Removable or DriveType.Network)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Best-effort : en cas de doute, on ne signale pas.
        }

        return false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
