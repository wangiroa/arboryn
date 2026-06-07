using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Triage;
using Arboryn.Domain.ValueObjects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// ViewModel du triage (Inc 7) : choisir un dossier, préparer (extraction texte/OCR, miniatures,
/// pré-remplissage source/objet/date), corriger en lot dans la grille, puis appliquer
/// (placement canonique des documents officiels, annulable) — l'apprentissage des patterns
/// tourne automatiquement après chaque lot.
/// </summary>
public sealed class TriageViewModel : INotifyPropertyChanged
{
    private static readonly string[] DefaultSubcategories =
    {
        "Investissements", "Fiscal", "Santé", "Banque", "Logement",
        "Assurance", "Travail", "Famille", "Véhicule", "Divers",
    };

    private readonly PrepareTriageHandler _prepare;
    private readonly ApplyTriageHandler _apply;
    private readonly LearnTriagePatternsHandler _learn;
    private readonly UndoUniformizationHandler _undo;
    private readonly ILogger<TriageViewModel> _logger;

    private string _currentFolder = "(aucun dossier sélectionné)";
    private string _statusText = "Choisissez un dossier de documents, puis lancez la préparation.";
    private bool _isBusy;
    private string? _selectedFolder;

    public TriageViewModel(
        PrepareTriageHandler prepare,
        ApplyTriageHandler apply,
        LearnTriagePatternsHandler learn,
        UndoUniformizationHandler undo,
        ILogger<TriageViewModel> logger)
    {
        _prepare = prepare;
        _apply = apply;
        _learn = learn;
        _undo = undo;
        _logger = logger;
    }

    /// <summary>Documents préparés, prêts à trier.</summary>
    public ObservableCollection<TriageRowItem> Rows { get; } = new();

    /// <summary>Sous-catégories de placement proposées (partagées par toutes les lignes).</summary>
    public ObservableCollection<string> Subcategories { get; } = new(DefaultSubcategories);

    public string CurrentFolder
    {
        get => _currentFolder;
        private set => SetProperty(ref _currentFolder, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanPrepare));
                OnPropertyChanged(nameof(CanApply));
                OnPropertyChanged(nameof(CanUndo));
            }
        }
    }

    public bool CanPrepare => !_isBusy && _selectedFolder is not null;

    public bool CanApply => !_isBusy && Rows.Any(r => r.IsValidated);

    public bool CanUndo => !_isBusy;

    public void SelectFolder(string folderPath)
    {
        _selectedFolder = folderPath;
        CurrentFolder = folderPath;
        Rows.Clear();
        OnPropertyChanged(nameof(CanPrepare));
        OnPropertyChanged(nameof(CanApply));
        StatusText = "Dossier sélectionné. Lancez la préparation pour analyser les documents.";
    }

    public async Task PrepareAsync()
    {
        if (!CanPrepare)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Préparation : extraction de texte, OCR et pré-remplissage…";
        try
        {
            var root = FilePath.From(_selectedFolder!);
            var thumbnailDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Arboryn", "thumbnails");

            var result = await Task.Run(() => _prepare.ExecuteAsync(VolumeId.Default, root, thumbnailDir));

            Rows.Clear();
            foreach (var candidate in result.Candidates)
            {
                Rows.Add(new TriageRowItem(candidate, Subcategories, OnRowValidationChanged));
            }

            OnPropertyChanged(nameof(CanApply));

            var ocrNote = result.OcrAvailable
                ? $"{result.OcrUsed} via OCR."
                : "OCR indisponible (tessdata absent) — les scans sans texte ne sont pas pré-remplis.";
            StatusText = result.Candidates.Count == 0
                ? "Aucun document à trier dans ce dossier (PDF et images scannées)."
                : $"{result.Candidates.Count} document(s) préparé(s). {ocrNote} Corrigez puis cochez « Validé », et appliquez.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de la préparation du triage");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyAsync()
    {
        if (!CanApply)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Application du triage : placement des documents…";
        try
        {
            var root = FilePath.From(_selectedFolder!);
            var decisions = Rows.Where(r => r.IsValidated).Select(r => r.ToDecision()).ToList();

            var result = await Task.Run(() => _apply.ExecuteAsync(decisions, root));

            // Apprentissage immédiat : les corrections de ce lot améliorent le lot suivant.
            var learned = await Task.Run(() => _learn.ExecuteAsync());

            // Retire les lignes appliquées de la grille.
            foreach (var applied in Rows.Where(r => r.IsValidated).ToList())
            {
                Rows.Remove(applied);
            }

            OnPropertyChanged(nameof(CanApply));

            var learnNote = learned > 0 ? $" {learned} pattern(s) appris." : string.Empty;
            StatusText = result.Failed == 0
                ? $"{result.Applied} document(s) classé(s) sous « Documents officiels ». Annulable.{learnNote}"
                : $"{result.Applied} classé(s), {result.Skipped} ignoré(s) (champ requis manquant), {result.Failed} échec(s).{learnNote}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'application du triage");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UndoAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Annulation du dernier lot…";
        try
        {
            var result = await Task.Run(() => _undo.ExecuteAsync());
            StatusText = !result.HadBatch
                ? "Rien à annuler."
                : result.Failed == 0
                    ? $"{result.Restored} document(s) remis à leur emplacement d'origine."
                    : $"{result.Restored} remis, {result.Failed} échec(s).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'annulation du triage");
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnRowValidationChanged() => OnPropertyChanged(nameof(CanApply));

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

/// <summary>Une ligne éditable de la grille de triage : miniature, extrait, et champs corrigeables.</summary>
public sealed class TriageRowItem : INotifyPropertyChanged
{
    private readonly FileInstanceId _instanceId;
    private readonly FilePath _path;
    private readonly FilePath? _thumbnailPath;
    private readonly string? _originalSource;
    private readonly string? _originalObject;
    private readonly Action _onValidationChanged;

    private string _source;
    private string _object;
    private string _date;
    private string _subcategory;
    private bool _isValidated;
    private ImageSource? _thumbnail;
    private bool _thumbnailLoaded;

    public TriageRowItem(TriageCandidate candidate, IReadOnlyList<string> subcategories, Action onValidationChanged)
    {
        _instanceId = candidate.InstanceId;
        _path = candidate.Path;
        _thumbnailPath = candidate.ThumbnailPath;
        _onValidationChanged = onValidationChanged;

        FileName = Path.GetFileName(candidate.Path.Value);
        Snippet = string.IsNullOrWhiteSpace(candidate.Snippet)
            ? "(aucun texte extrait — scan sans OCR)"
            : candidate.Snippet;
        Subcategories = subcategories;

        _source = candidate.Extraction.Source.Value ?? string.Empty;
        _object = candidate.Extraction.Object.Value ?? string.Empty;
        _date = candidate.Extraction.Date.Value ?? string.Empty;
        _subcategory = subcategories.FirstOrDefault() ?? "Divers";

        _originalSource = candidate.Extraction.Source.Value;
        _originalObject = candidate.Extraction.Object.Value;
    }

    public string FileName { get; }

    public string Snippet { get; }

    public IReadOnlyList<string> Subcategories { get; }

    public string Source { get => _source; set => SetProperty(ref _source, value); }

    public string Object { get => _object; set => SetProperty(ref _object, value); }

    public string Date { get => _date; set => SetProperty(ref _date, value); }

    public string Subcategory { get => _subcategory; set => SetProperty(ref _subcategory, value); }

    public bool IsValidated
    {
        get => _isValidated;
        set
        {
            if (SetProperty(ref _isValidated, value))
            {
                _onValidationChanged();
            }
        }
    }

    public Visibility ThumbnailVisibility => _thumbnailPath is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility GlyphVisibility => _thumbnailPath is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Miniature rendue (PNG sur disque), chargée paresseusement à la première lecture.</summary>
    public ImageSource? Thumbnail
    {
        get
        {
            if (_thumbnailLoaded)
            {
                return _thumbnail;
            }

            _thumbnailLoaded = true;
            if (_thumbnailPath is { } thumb)
            {
                try
                {
                    _thumbnail = new BitmapImage(new Uri(thumb.Value)) { DecodePixelWidth = 200 };
                }
                catch (Exception)
                {
                    _thumbnail = null;
                }
            }

            return _thumbnail;
        }
    }

    /// <summary>Construit la décision de triage à appliquer (date normalisée en yyyyMM si possible).</summary>
    public TriageDecision ToDecision()
    {
        var date = FrenchDateParser.TryParse(_date, out var yyyyMM) ? yyyyMM : _date.Trim();
        return new TriageDecision(
            _instanceId, _path, _source.Trim(), _object.Trim(), date, _subcategory.Trim(),
            Snippet, _originalSource, _originalObject);
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;
}
