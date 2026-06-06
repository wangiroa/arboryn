using Arboryn.Domain.Enums;

namespace Arboryn.Domain.Taxonomy;

/// <summary>
/// Templates canoniques livrés par défaut pour chaque catégorie (syntaxe Scriban). Servent
/// tant que l'utilisateur n'a pas personnalisé la taxonomie d'une catégorie. Les champs
/// référencés correspondent aux clés de métadonnées extraites (Inc 4) plus <c>ext</c>.
/// </summary>
public static class DefaultTaxonomies
{
    /// <summary>Taxonomie par défaut d'une catégorie, ou <c>null</c> si elle n'est pas uniformisable.</summary>
    public static CategoryTaxonomy? For(MediaCategory category) => category switch
    {
        MediaCategory.Audiobook => new CategoryTaxonomy(
            category,
            PathTemplate: "Livres audio/{{ author }}{{ if series }}/{{ series }}{{ end }}",
            NameTemplate: "{{ author }} - {{ if series }}{{ series }} - {{ volume | format \"00\" }} - {{ end }}{{ title }}.{{ ext }}",
            RequiredFields: new[] { "author", "title" }),

        MediaCategory.Book => new CategoryTaxonomy(
            category,
            PathTemplate: "Livres/{{ author }}{{ if series }}/{{ series }}{{ end }}",
            NameTemplate: "{{ author }} - {{ if series }}{{ series }} - {{ volume | format \"00\" }} - {{ end }}{{ title }}.{{ ext }}",
            RequiredFields: new[] { "author", "title" }),

        MediaCategory.Video => new CategoryTaxonomy(
            category,
            PathTemplate: "Vidéos/{{ title }}{{ if year }} ({{ year }}){{ end }}",
            NameTemplate: "{{ title }}{{ if year }} ({{ year }}){{ end }}.{{ ext }}",
            RequiredFields: new[] { "title" }),

        MediaCategory.Photo => new CategoryTaxonomy(
            category,
            PathTemplate: "Photos/{{ year }}",
            NameTemplate: "{{ date_taken }}.{{ ext }}",
            RequiredFields: new[] { "year", "date_taken" }),

        MediaCategory.OfficialDocument => new CategoryTaxonomy(
            category,
            PathTemplate: "Documents officiels/{{ subcategory }}",
            NameTemplate: "[{{ source }}] - {{ object }} - {{ date }}.{{ ext }}",
            RequiredFields: new[] { "source", "object", "date" }),

        MediaCategory.OtherDocument => new CategoryTaxonomy(
            category,
            PathTemplate: "PDF divers",
            NameTemplate: "{{ title }}.{{ ext }}",
            RequiredFields: new[] { "title" }),

        _ => null,
    };
}
