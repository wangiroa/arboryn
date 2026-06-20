using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Arboryn.Application.UseCases;
using Microsoft.Extensions.Logging;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// ViewModel de la révision des candidats d'enrichissement (suivi Inc 8) : liste les champs
/// proposés en ligne sous le seuil d'auto-application et permet de les accepter (écriture en
/// <c>file_metadata</c>) ou de les rejeter, un par un ou tous d'un coup.
/// </summary>
public sealed class EnrichmentReviewViewModel : INotifyPropertyChanged
{
    private readonly ReviewEnrichmentCandidatesHandler _review;
    private readonly ILogger<EnrichmentReviewViewModel> _logger;

    private bool _isBusy;
    private string _statusText = "Aucun candidat chargé.";

    public EnrichmentReviewViewModel(
        ReviewEnrichmentCandidatesHandler review,
        ILogger<EnrichmentReviewViewModel> logger)
    {
        _review = review;
        _logger = logger;
    }

    /// <summary>Candidats en attente de décision.</summary>
    public ObservableCollection<EnrichmentCandidateRowItem> Rows { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanAct));
            }
        }
    }

    public bool CanAct => !_isBusy;

    /// <summary>Vrai quand il n'y a aucun candidat à réviser (affiche l'état vide).</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Inverse de <see cref="IsEmpty"/> — pilote la visibilité de la grille.</summary>
    public bool HasCandidates => Rows.Count > 0;

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    /// <summary>(Re)charge les candidats en attente depuis la base.</summary>
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var pending = await _review.ListPendingAsync();
            Rows.Clear();
            foreach (var c in pending)
            {
                Rows.Add(new EnrichmentCandidateRowItem(c.Id, c.Path, c.Provider, c.Key, c.Value, c.Confidence));
            }
            StatusText = Rows.Count == 0
                ? "Aucun candidat en attente. Lancez un enrichissement depuis les Réglages."
                : $"{Rows.Count} champ(s) proposé(s) à valider.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du chargement des candidats d'enrichissement");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasCandidates));
        }
    }

    /// <summary>Accepte un candidat : la valeur est écrite dans les métadonnées du fichier.</summary>
    public Task AcceptAsync(EnrichmentCandidateRowItem row) => DecideAsync(row, accept: true);

    /// <summary>Rejette un candidat : il est marqué rejeté et retiré de la liste.</summary>
    public Task RejectAsync(EnrichmentCandidateRowItem row) => DecideAsync(row, accept: false);

    /// <summary>Accepte tous les candidats affichés.</summary>
    public async Task AcceptAllAsync()
    {
        if (IsBusy || Rows.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var accepted = 0;
            foreach (var row in Rows.ToArray())
            {
                if (await _review.AcceptAsync(row.Id))
                {
                    accepted++;
                }
                Rows.Remove(row);
            }
            StatusText = $"{accepted} champ(s) accepté(s) et écrit(s) dans les métadonnées.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'acceptation en lot");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasCandidates));
        }
    }

    private async Task DecideAsync(EnrichmentCandidateRowItem row, bool accept)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var ok = accept ? await _review.AcceptAsync(row.Id) : await _review.RejectAsync(row.Id);
            Rows.Remove(row);
            StatusText = ok
                ? (accept
                    ? $"« {row.FieldLabel} » accepté et écrit dans les métadonnées."
                    : $"« {row.FieldLabel} » rejeté.")
                : "Ce candidat n'existe plus.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de la décision sur un candidat");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasCandidates));
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

/// <summary>Une ligne de candidat d'enrichissement dans la grille de révision (immuable).</summary>
public sealed class EnrichmentCandidateRowItem
{
    public EnrichmentCandidateRowItem(
        string id, string path, string provider, string key, string value, double confidence)
    {
        Id = id;
        Path = path;
        Provider = provider;
        Key = key;
        Value = value;
        Confidence = confidence;
    }

    public string Id { get; }

    public string Path { get; }

    public string Provider { get; }

    public string Key { get; }

    public string Value { get; }

    public double Confidence { get; }

    /// <summary>Nom de fichier seul (le chemin complet est en info-bulle).</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Libellé lisible du champ (ex. <c>album_artist</c> → « Artiste de l'album »).</summary>
    public string FieldLabel => FriendlyField(Key);

    /// <summary>Confiance formatée en pourcentage pour l'affichage.</summary>
    public string ConfidenceText => Confidence.ToString("P0", CultureInfo.CurrentCulture);

    /// <summary>Provenance lisible (ex. « via OpenLibrary »).</summary>
    public string ProviderText => $"via {FriendlyProvider(Provider)}";

    private static string FriendlyField(string key) => key switch
    {
        "title" => "Titre",
        "subtitle" => "Sous-titre",
        "author" => "Auteur",
        "artist" => "Artiste",
        "album" => "Album",
        "album_artist" => "Artiste de l'album",
        "year" => "Année",
        "date" => "Date",
        "publisher" => "Éditeur",
        "language" => "Langue",
        "isbn" => "ISBN",
        "genre" => "Genre",
        "series" => "Série",
        _ => key,
    };

    private static string FriendlyProvider(string provider) => provider switch
    {
        "openlibrary" => "OpenLibrary",
        "googlebooks" => "Google Books",
        "tmdb" => "TMDB",
        "musicbrainz" => "MusicBrainz",
        _ => provider,
    };
}
