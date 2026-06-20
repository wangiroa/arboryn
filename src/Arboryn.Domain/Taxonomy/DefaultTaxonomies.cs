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
            // Œuvre multi-fichiers (chapitre présent) : « Titre œuvre - chapitre » (le titre vient
            // du dossier, cf. TemplateFields). Sinon : « Auteur - [Série - NN - ]Titre ».
            NameTemplate: "{{ if chapter }}{{ title }} - {{ chapter }}{{ else }}{{ author }} - {{ if series }}{{ series }} - {{ volume | format \"00\" }} - {{ end }}{{ title }}{{ end }}.{{ ext }}",
            RequiredFields: new[] { "author", "title" }),

        MediaCategory.Book => new CategoryTaxonomy(
            category,
            PathTemplate: "Livres/{{ author }}{{ if series }}/{{ series }}{{ end }}",
            NameTemplate: "{{ author }} - {{ if series }}{{ series }} - {{ volume | format \"00\" }} - {{ end }}{{ title }}.{{ ext }}",
            RequiredFields: new[] { "author", "title" }),

        // Comics / BD : une série est souvent découpée en plusieurs fichiers (un par tome).
        // Le titre de la série vient du dossier (cf. TemplateFields) et chaque fichier porte un
        // numéro zero-paddé : « Bandes dessinées/<Série>/<Série> - 001.cbz ».
        MediaCategory.Comic => new CategoryTaxonomy(
            category,
            PathTemplate: "Bandes dessinées/{{ title }}",
            NameTemplate: "{{ if chapter }}{{ title }} - {{ chapter }}{{ else }}{{ title }}{{ end }}.{{ ext }}",
            RequiredFields: new[] { "title" }),

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

    /// <summary>
    /// Anciens templates de défaut désormais remplacés par une version plus récente de
    /// <see cref="For"/>. Conservés pour reconnaître une taxonomie stockée qui n'est qu'un
    /// ancien défaut non personnalisé, afin de la laisser repasser au défaut courant
    /// (cf. <see cref="IsShippedDefault"/>).
    ///
    /// CONTRAT : à chaque évolution d'un template de <see cref="For"/>, ajouter ici l'ancien
    /// template (tel qu'il était livré) pour que les bases existantes se mettent à jour.
    /// </summary>
    private static readonly IReadOnlyList<CategoryTaxonomy> SupersededDefaults = new[]
    {
        // Livres audio — avant l'ajout de la branche {{ if chapter }} (œuvres multi-fichiers,
        // 2026-06-07). L'ancien template ne portait pas le numéro de chapitre.
        new CategoryTaxonomy(
            MediaCategory.Audiobook,
            PathTemplate: "Livres audio/{{ author }}{{ if series }}/{{ series }}{{ end }}",
            NameTemplate: "{{ author }} - {{ if series }}{{ series }} - {{ volume | format \"00\" }} - {{ end }}{{ title }}.{{ ext }}",
            RequiredFields: new[] { "author", "title" }),
    };

    /// <summary>
    /// Vrai si <paramref name="candidate"/> correspond — aux templates et champs requis près,
    /// la version étant ignorée — à un défaut livré pour sa catégorie, courant ou ancien.
    /// Permet de distinguer une taxonomie stockée non personnalisée (à laisser suivre le
    /// défaut du code) d'une véritable personnalisation utilisateur (à préserver).
    /// </summary>
    public static bool IsShippedDefault(CategoryTaxonomy candidate)
    {
        var current = For(candidate.Category);
        if (current is not null && TemplatesMatch(current, candidate))
        {
            return true;
        }

        foreach (var legacy in SupersededDefaults)
        {
            if (legacy.Category == candidate.Category && TemplatesMatch(legacy, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TemplatesMatch(CategoryTaxonomy a, CategoryTaxonomy b)
        => string.Equals(a.PathTemplate, b.PathTemplate, StringComparison.Ordinal)
           && string.Equals(a.NameTemplate, b.NameTemplate, StringComparison.Ordinal)
           && a.RequiredFields.SequenceEqual(b.RequiredFields, StringComparer.Ordinal);
}
