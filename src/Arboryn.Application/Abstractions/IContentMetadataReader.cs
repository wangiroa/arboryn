using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Lecteur de métadonnées issues du <em>contenu</em> d'un fichier : tags audio
/// (ID3/Xiph/Apple), dictionnaire Info d'un PDF, OPF d'un EPUB, EXIF d'une image…
///
/// Chaque lecteur déclare la (les) catégorie(s) qu'il sait traiter, la source à
/// attribuer aux entrées produites (cf. <see cref="MetadataSources"/>) et une
/// confiance de base. L'orchestrateur (<c>ExtractMetadataHandler</c>) applique
/// tous les lecteurs dont <see cref="CanRead"/> est vrai pour la catégorie du
/// fichier et isole les échecs individuels.
/// </summary>
public interface IContentMetadataReader
{
    /// <summary>Source attribuée aux entrées produites (cf. <see cref="MetadataSources"/>).</summary>
    string Source { get; }

    /// <summary>Confiance de base des entrées de ce lecteur (0..1).</summary>
    double Confidence { get; }

    /// <summary>Vrai si ce lecteur sait extraire des métadonnées pour cette catégorie.</summary>
    bool CanRead(MediaCategory category);

    /// <summary>
    /// Lit les métadonnées de contenu et renvoie un dictionnaire clé canonique →
    /// valeur (conventions de <see cref="MetadataKeys"/>). Peut lever : l'appelant
    /// isole les erreurs par fichier.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ReadAsync(FilePath path, CancellationToken cancellationToken);
}
