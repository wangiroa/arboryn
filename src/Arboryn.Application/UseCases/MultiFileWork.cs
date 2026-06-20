using System.Globalization;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Matching;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Logique des <b>œuvres multi-fichiers</b> : livres audio découpés en pistes et séries de
/// comics/BD découpées en tomes, où l'identité de l'œuvre est portée par le dossier parent
/// et chaque fichier ne désigne qu'une position dans la série.
///
/// Centralise trois décisions, partagées par <see cref="TemplateFields"/> (titre tiré du
/// dossier) et <see cref="PlanUniformizationHandler"/> (numérotation au niveau de l'œuvre) :
/// quelles catégories sont concernées, si un fichier donné est une « partie » d'œuvre, et
/// comment numéroter les parties d'une même œuvre à largeur constante. Pur et sans I/O.
/// </summary>
public static class MultiFileWork
{
    /// <summary>Catégories dont une œuvre peut être découpée en plusieurs fichiers.</summary>
    private static readonly HashSet<MediaCategory> Categories = new()
    {
        MediaCategory.Audiobook,
        MediaCategory.Comic,
    };

    public static bool IsMultiFileCategory(MediaCategory category) => Categories.Contains(category);

    /// <summary>
    /// Vrai si ce fichier n'est qu'une partie d'une œuvre multi-fichiers : sa catégorie s'y
    /// prête et son <b>nom de fichier</b> ne porte qu'une position (« 001 Chapitre 1 »,
    /// « Track 03 », « 01 »), sans identité d'œuvre propre.
    ///
    /// La décision repose sur le NOM DE FICHIER, pas sur le tag titre : beaucoup d'audiobooks
    /// répètent le titre de l'œuvre dans le tag Title de chaque piste — s'y fier ferait passer
    /// chaque chapitre pour une œuvre mono-fichier (tous identiques → collisions « (2) »).
    /// </summary>
    public static bool IsPartFile(MediaCategory category, string? fileStem)
        => Categories.Contains(category)
           && (string.IsNullOrWhiteSpace(fileStem) || PartitionMarkers.IsPositionOnly(fileStem!));

    /// <summary>
    /// Titre de l'œuvre déduit du nom de dossier parent, débarrassé de l'auteur connu
    /// (gère « Titre - Auteur » comme « Auteur - Titre »). Renvoie le dossier tel quel si
    /// l'auteur n'y apparaît pas.
    /// </summary>
    public static string WorkTitle(string parentDirectoryName, string? author)
    {
        var title = parentDirectoryName.Trim();
        if (!string.IsNullOrWhiteSpace(author))
        {
            var index = title.IndexOf(author, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                title = title.Remove(index, author.Length);
            }
        }

        return title.Trim(' ', '-', '_', '.', '\t');
    }

    /// <summary>
    /// Numéro de séquence d'un fichier : le nombre porté par son nom (« 001 chapitre 1 » → 1)
    /// ou, à défaut, le numéro de piste de ses tags. <c>null</c> si aucun.
    /// </summary>
    public static int? SequenceNumber(string fileStem, string? trackTag)
    {
        var fromName = PartitionMarkers.FirstNumber(fileStem);
        if (fromName.HasValue)
        {
            return fromName;
        }

        return int.TryParse(trackTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var track)
            ? track
            : null;
    }

    /// <summary>
    /// Attribue à chaque fichier d'une même œuvre un numéro zero-paddé à largeur constante,
    /// dans l'ordre d'entrée. Si tous les fichiers portent un numéro propre, on le conserve
    /// (la largeur s'aligne sur le plus grand) ; sinon on renumérote de 1 à N par ordre
    /// alphabétique du nom. La largeur garantit le bon tri alphabétique (« 001 » … « 010 »).
    /// </summary>
    public static IReadOnlyList<string> NumberParts(IReadOnlyList<(string Stem, string? TrackTag)> files)
    {
        if (files.Count == 0)
        {
            return Array.Empty<string>();
        }

        var numbers = files.Select(f => SequenceNumber(f.Stem, f.TrackTag)).ToList();

        int[] assigned;
        if (numbers.All(n => n.HasValue))
        {
            assigned = numbers.Select(n => n!.Value).ToArray();
        }
        else
        {
            // Numérotation de secours : tri alphabétique stable, puis 1..N.
            assigned = new int[files.Count];
            var order = Enumerable.Range(0, files.Count)
                .OrderBy(i => files[i].Stem, StringComparer.OrdinalIgnoreCase)
                .ToList();
            for (var rank = 0; rank < order.Count; rank++)
            {
                assigned[order[rank]] = rank + 1;
            }
        }

        var width = assigned.Max().ToString(CultureInfo.InvariantCulture).Length;
        var format = new string('0', width);
        return assigned.Select(a => a.ToString(format, CultureInfo.InvariantCulture)).ToArray();
    }
}
