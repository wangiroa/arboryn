using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Calcule l'empreinte perceptuelle 64 bits d'un média visuel (image, ou vidéo via
/// l'agrégation de ses keyframes). Chaque implémentation déclare la (les) catégorie(s)
/// qu'elle sait traiter. Renvoie <c>null</c> si le fichier n'est pas exploitable (format
/// inconnu, outil externe absent…).
/// </summary>
public interface IPerceptualHasher
{
    bool CanHash(MediaCategory category);

    Task<PerceptualHash?> ComputeAsync(FilePath path, CancellationToken cancellationToken);
}
