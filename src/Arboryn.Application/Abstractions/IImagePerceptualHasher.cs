using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Calcule l'empreinte perceptuelle d'une image. Renvoie <c>null</c> si le fichier
/// n'est pas une image décodable (format inconnu, fichier corrompu…).
/// </summary>
public interface IImagePerceptualHasher
{
    Task<PerceptualHash?> ComputeAsync(FilePath path, CancellationToken cancellationToken);
}
