using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Abstraction de la corbeille Windows. La suppression renvoie l'emplacement du
/// fichier dans la corbeille (s'il a pu être capturé), ce qui permet une
/// restauration ultérieure par déplacement inverse.
/// </summary>
public interface IRecycleBin
{
    /// <summary>
    /// Envoie un fichier à la corbeille. Renvoie son chemin dans la corbeille
    /// (pour permettre l'undo), ou <c>null</c> si ce chemin n'a pas pu être capturé.
    /// </summary>
    Task<FilePath?> SendToRecycleBinAsync(FilePath path, CancellationToken cancellationToken);

    /// <summary>
    /// Restaure un fichier depuis la corbeille vers son emplacement d'origine.
    /// Renvoie <c>false</c> si la restauration a échoué (fichier déjà vidé, conflit…).
    /// </summary>
    Task<bool> RestoreAsync(FilePath recycledPath, FilePath originalPath, CancellationToken cancellationToken);
}
