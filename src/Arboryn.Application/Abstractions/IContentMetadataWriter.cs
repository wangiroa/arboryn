using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Écrit des métadonnées canoniques dans le fichier lui-même (write-back). Chaque
/// implémentation déclare la (les) catégorie(s) qu'elle sait écrire (tags audio, OPF, EXIF…).
/// </summary>
public interface IContentMetadataWriter
{
    bool CanWrite(MediaCategory category);

    /// <summary>
    /// Écrit les champs supportés présents dans <paramref name="fields"/> (clés
    /// <see cref="MetadataKeys"/>) et renvoie les valeurs <em>précédentes</em> de ces champs,
    /// pour permettre l'annulation. Une valeur <c>null</c> dans <paramref name="fields"/> efface le tag.
    /// </summary>
    Task<IReadOnlyDictionary<string, string?>> WriteAsync(
        FilePath path, IReadOnlyDictionary<string, string?> fields, CancellationToken cancellationToken);
}
