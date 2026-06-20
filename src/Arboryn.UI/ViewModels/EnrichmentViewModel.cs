using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// ViewModel de l'enrichissement en ligne (Inc 8) : réglages privacy-first (interrupteur global
/// et par catégorie, seuil d'auto-application, clés d'API, enrichissement pendant le scan) et
/// déclenchement à la demande sur tout le catalogue ou un dossier choisi.
/// </summary>
public sealed class EnrichmentViewModel : INotifyPropertyChanged
{
    private readonly ISettingsRepository _settings;
    private readonly IEnrichmentKeyring _keyring;
    private readonly EnrichDirectoryHandler _enrich;
    private readonly ILogger<EnrichmentViewModel> _logger;

    private bool _onlineModeEnabled;
    private bool _enrichDuringScan;
    private double _confidenceThreshold = 0.9;
    private bool _enrichBooks = true;
    private bool _enrichAudiobooks = true;
    private bool _enrichVideos = true;
    private bool _enrichComics = true;
    private string _tmdbApiKey = string.Empty;
    private string _googleBooksApiKey = string.Empty;
    private bool _isBusy;
    private string _statusText = "Enrichissement désactivé par défaut (mode local). Activez-le et enregistrez.";

    public EnrichmentViewModel(
        ISettingsRepository settings,
        IEnrichmentKeyring keyring,
        EnrichDirectoryHandler enrich,
        ILogger<EnrichmentViewModel> logger)
    {
        _settings = settings;
        _keyring = keyring;
        _enrich = enrich;
        _logger = logger;
    }

    public bool OnlineModeEnabled { get => _onlineModeEnabled; set => SetProperty(ref _onlineModeEnabled, value); }

    public bool EnrichDuringScan { get => _enrichDuringScan; set => SetProperty(ref _enrichDuringScan, value); }

    public double ConfidenceThreshold { get => _confidenceThreshold; set => SetProperty(ref _confidenceThreshold, value); }

    public bool EnrichBooks { get => _enrichBooks; set => SetProperty(ref _enrichBooks, value); }

    public bool EnrichAudiobooks { get => _enrichAudiobooks; set => SetProperty(ref _enrichAudiobooks, value); }

    public bool EnrichVideos { get => _enrichVideos; set => SetProperty(ref _enrichVideos, value); }

    public bool EnrichComics { get => _enrichComics; set => SetProperty(ref _enrichComics, value); }

    public string TmdbApiKey { get => _tmdbApiKey; set => SetProperty(ref _tmdbApiKey, value); }

    public string GoogleBooksApiKey { get => _googleBooksApiKey; set => SetProperty(ref _googleBooksApiKey, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRun));
            }
        }
    }

    public bool CanRun => !_isBusy;

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    /// <summary>Charge les réglages persistés dans les propriétés bindées.</summary>
    public async Task LoadAsync()
    {
        OnlineModeEnabled = await ReadBoolAsync("online_mode_enabled", false);
        EnrichDuringScan = await ReadBoolAsync("enrich_during_scan", false);
        ConfidenceThreshold = await ReadDoubleAsync("confidence_auto_apply", 0.9);
        EnrichBooks = await ReadBoolAsync("online_mode_book", true);
        EnrichAudiobooks = await ReadBoolAsync("online_mode_audiobook", true);
        EnrichVideos = await ReadBoolAsync("online_mode_video", true);
        EnrichComics = await ReadBoolAsync("online_mode_comic", true);
        TmdbApiKey = await _settings.GetAsync("api_key_tmdb", CancellationToken.None) ?? string.Empty;
        GoogleBooksApiKey = await _settings.GetAsync("api_key_googlebooks", CancellationToken.None) ?? string.Empty;
    }

    /// <summary>Persiste tous les réglages et recharge le trousseau de clés.</summary>
    public async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            await _settings.SetAsync("online_mode_enabled", Bool(OnlineModeEnabled), CancellationToken.None);
            await _settings.SetAsync("enrich_during_scan", Bool(EnrichDuringScan), CancellationToken.None);
            await _settings.SetAsync("confidence_auto_apply",
                ConfidenceThreshold.ToString("0.00", CultureInfo.InvariantCulture), CancellationToken.None);
            await _settings.SetAsync("online_mode_book", Bool(EnrichBooks), CancellationToken.None);
            await _settings.SetAsync("online_mode_audiobook", Bool(EnrichAudiobooks), CancellationToken.None);
            await _settings.SetAsync("online_mode_video", Bool(EnrichVideos), CancellationToken.None);
            await _settings.SetAsync("online_mode_comic", Bool(EnrichComics), CancellationToken.None);
            await _settings.SetAsync("api_key_tmdb", TmdbApiKey.Trim(), CancellationToken.None);
            await _settings.SetAsync("api_key_googlebooks", GoogleBooksApiKey.Trim(), CancellationToken.None);

            await _keyring.RefreshAsync(CancellationToken.None);
            StatusText = "Réglages d'enrichissement enregistrés.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'enregistrement des réglages d'enrichissement");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Enrichit tout le catalogue du volume par défaut.</summary>
    public Task EnrichCatalogAsync()
        => RunAsync(progress => _enrich.ExecuteCatalogAsync(VolumeId.Default, progress));

    /// <summary>Enrichit les fichiers sous un dossier choisi.</summary>
    public Task EnrichFolderAsync(string folderPath)
        => RunAsync(progress => _enrich.ExecuteAsync(VolumeId.Default, FilePath.From(folderPath), progress));

    private async Task RunAsync(Func<IProgress<int>, Task<EnrichDirectoryResult>> run)
    {
        if (IsBusy)
        {
            return;
        }

        if (!OnlineModeEnabled)
        {
            StatusText = "Mode en ligne désactivé : activez-le et enregistrez avant d'enrichir.";
            return;
        }

        IsBusy = true;
        StatusText = "Enrichissement en cours…";
        try
        {
            var progress = new Progress<int>(n => StatusText = $"Enrichissement : {n} fichier(s) traité(s)…");
            var result = await Task.Run(() => run(progress));
            StatusText = $"{result.AppliedFields} champ(s) appliqué(s) sur {result.EnrichedFiles} fichier(s) " +
                         $"({result.Processed} examiné(s)). {result.Candidates.Count} candidat(s) sous le seuil.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'enrichissement à la demande");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> ReadBoolAsync(string key, bool fallback)
    {
        var raw = await _settings.GetAsync(key, CancellationToken.None);
        return raw is null ? fallback : string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<double> ReadDoubleAsync(string key, double fallback)
    {
        var raw = await _settings.GetAsync(key, CancellationToken.None);
        return raw is not null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : fallback;
    }

    private static string Bool(bool value) => value ? "true" : "false";

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
