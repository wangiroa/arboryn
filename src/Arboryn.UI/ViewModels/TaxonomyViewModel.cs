using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Taxonomy;
using Microsoft.Extensions.Logging;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// ViewModel de l'éditeur de taxonomie (Inc 6) : éditer les templates de chemin/nom et les
/// champs requis par catégorie, prévisualiser le rendu sur un exemple, et enregistrer une
/// nouvelle version active.
/// </summary>
public sealed class TaxonomyViewModel : INotifyPropertyChanged
{
    private readonly ITaxonomyRepository _repository;
    private readonly ITemplateRenderer _renderer;
    private readonly ILogger<TaxonomyViewModel> _logger;
    private string _statusText = "Chargement…";

    public TaxonomyViewModel(
        ITaxonomyRepository repository,
        ITemplateRenderer renderer,
        ILogger<TaxonomyViewModel> logger)
    {
        _repository = repository;
        _renderer = renderer;
        _logger = logger;
    }

    public ObservableCollection<TaxonomyItem> Categories { get; } = new();

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public async Task LoadAsync()
    {
        try
        {
            var taxonomies = await Task.Run(() => _repository.GetAllAsync(System.Threading.CancellationToken.None));
            Categories.Clear();
            foreach (var taxonomy in taxonomies.OrderBy(t => (int)t.Category))
            {
                var item = new TaxonomyItem(taxonomy);
                Preview(item); // aperçu initial
                Categories.Add(item);
            }

            StatusText = $"{Categories.Count} catégorie(s). Modifiez un template puis « Aperçu » ou « Enregistrer ».";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du chargement de la taxonomie");
            StatusText = $"Erreur : {ex.Message}";
        }
    }

    /// <summary>Rend les templates de l'item sur un jeu d'exemple et met à jour son aperçu.</summary>
    public void Preview(TaxonomyItem item)
    {
        try
        {
            var sample = SampleFields.For(item.Category);
            var directory = _renderer.Render(item.PathTemplate, sample);
            var name = _renderer.Render(item.NameTemplate, sample);
            item.PreviewText = WindowsPathSanitizer.SanitizeRelativeDirectory(directory) + "\\" +
                               WindowsPathSanitizer.SanitizeFileName(name);
        }
        catch (Exception ex)
        {
            item.PreviewText = $"Template invalide : {ex.Message}";
        }
    }

    public async Task SaveAsync(TaxonomyItem item)
    {
        try
        {
            var required = item.RequiredFieldsCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var taxonomy = new CategoryTaxonomy(item.Category, item.PathTemplate, item.NameTemplate, required);

            var version = await Task.Run(() => _repository.SaveAsync(taxonomy, System.Threading.CancellationToken.None));
            Preview(item);
            item.StatusText = $"Enregistré (version {version}). Relancez un aperçu dans « Uniformisation » pour ré-évaluer les chemins.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'enregistrement de la taxonomie {Category}", item.Category);
            item.StatusText = $"Erreur : {ex.Message}";
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

/// <summary>Ligne éditable de l'éditeur de taxonomie pour une catégorie.</summary>
public sealed class TaxonomyItem : INotifyPropertyChanged
{
    private string _pathTemplate;
    private string _nameTemplate;
    private string _requiredFieldsCsv;
    private string _previewText = string.Empty;
    private string _statusText = string.Empty;

    public TaxonomyItem(CategoryTaxonomy taxonomy)
    {
        Category = taxonomy.Category;
        CategoryLabel = LabelFor(taxonomy.Category);
        _pathTemplate = taxonomy.PathTemplate;
        _nameTemplate = taxonomy.NameTemplate;
        _requiredFieldsCsv = string.Join(", ", taxonomy.RequiredFields);
    }

    public MediaCategory Category { get; }

    public string CategoryLabel { get; }

    public string PathTemplate
    {
        get => _pathTemplate;
        set => SetProperty(ref _pathTemplate, value);
    }

    public string NameTemplate
    {
        get => _nameTemplate;
        set => SetProperty(ref _nameTemplate, value);
    }

    public string RequiredFieldsCsv
    {
        get => _requiredFieldsCsv;
        set => SetProperty(ref _requiredFieldsCsv, value);
    }

    public string PreviewText
    {
        get => _previewText;
        set => SetProperty(ref _previewText, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private static string LabelFor(MediaCategory category) => category switch
    {
        MediaCategory.Audiobook => "Livres audio",
        MediaCategory.Book => "Livres",
        MediaCategory.Video => "Vidéos",
        MediaCategory.Photo => "Photos",
        MediaCategory.OfficialDocument => "Documents officiels",
        MediaCategory.OtherDocument => "PDF divers",
        _ => category.ToString(),
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>Jeux de champs d'exemple par catégorie, pour l'aperçu du rendu des templates.</summary>
internal static class SampleFields
{
    public static IReadOnlyDictionary<string, string?> For(MediaCategory category) => category switch
    {
        MediaCategory.Audiobook => new Dictionary<string, string?>
        {
            ["author"] = "Asimov", ["series"] = "Fondation", ["volume"] = "1", ["title"] = "Fondation", ["ext"] = "m4b",
        },
        MediaCategory.Book => new Dictionary<string, string?>
        {
            ["author"] = "Tolkien", ["series"] = "Le Seigneur des Anneaux", ["volume"] = "1",
            ["title"] = "La Communauté de l'Anneau", ["ext"] = "epub",
        },
        MediaCategory.Video => new Dictionary<string, string?>
        {
            ["title"] = "Inception", ["year"] = "2010", ["ext"] = "mkv",
        },
        MediaCategory.Photo => new Dictionary<string, string?>
        {
            ["year"] = "2023", ["date_taken"] = "2023-07-14 18-30-00", ["ext"] = "jpg",
        },
        MediaCategory.OfficialDocument => new Dictionary<string, string?>
        {
            ["subcategory"] = "Investissements/Appartement", ["source"] = "EDF", ["object"] = "Facture",
            ["date"] = "202403", ["ext"] = "pdf",
        },
        _ => new Dictionary<string, string?> { ["title"] = "Document", ["ext"] = "pdf" },
    };
}
