using System.Collections.Generic;
using Arboryn.Domain.Enums;

namespace Arboryn.UI.ViewModels;

/// <summary>Libellés FR des catégories média, partagés par les écrans (Inc 10).</summary>
public static class CategoryLabels
{
    /// <summary>Catégories éligibles à un périmètre de réplication (hors <see cref="MediaCategory.Unknown"/>).</summary>
    public static readonly IReadOnlyList<MediaCategory> ScopeCategories = new[]
    {
        MediaCategory.Audiobook,
        MediaCategory.Book,
        MediaCategory.Comic,
        MediaCategory.Video,
        MediaCategory.Photo,
        MediaCategory.OfficialDocument,
        MediaCategory.OtherDocument,
    };

    public static string Of(MediaCategory category) => category switch
    {
        MediaCategory.Audiobook => "Livres audio",
        MediaCategory.Book => "Livres",
        MediaCategory.Comic => "Bandes dessinées",
        MediaCategory.Video => "Vidéos",
        MediaCategory.Photo => "Photos",
        MediaCategory.OfficialDocument => "Documents officiels",
        MediaCategory.OtherDocument => "PDF divers",
        _ => "Inconnu",
    };
}
