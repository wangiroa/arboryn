using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Déplacement/renommage de fichiers sur le volume (uniformisation intra-volume). Crée
/// l'arborescence cible au besoin. <see cref="Exists"/> sert à la résolution de conflits
/// lors de la planification.
/// </summary>
public interface IFileMover
{
    bool Exists(FilePath path);

    /// <summary>Déplace <paramref name="source"/> vers <paramref name="target"/> (sans écraser une cible existante).</summary>
    Task MoveAsync(FilePath source, FilePath target, CancellationToken cancellationToken);

    /// <summary>
    /// Copie <paramref name="source"/> vers <paramref name="target"/> (réplication inter-volume,
    /// Inc 10) sans écraser une cible existante. Crée l'arborescence cible au besoin.
    /// </summary>
    Task CopyAsync(FilePath source, FilePath target, CancellationToken cancellationToken);
}
