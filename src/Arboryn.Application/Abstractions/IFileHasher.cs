using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>Calcule l'empreinte de contenu d'un fichier (à la demande).</summary>
public interface IFileHasher
{
    Task<Sha256> ComputeAsync(FilePath path, CancellationToken cancellationToken);
}
