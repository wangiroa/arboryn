using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Cache persistant des empreintes SHA-256 par FileInstance (colonne
/// <c>file_instances.sha256</c>). Le hash est invalidé automatiquement si le fichier
/// change (taille/date) lors d'un re-scan.
/// </summary>
public interface IFileHashStore
{
    Task<Sha256?> GetAsync(FileInstanceId id, CancellationToken cancellationToken);

    Task SetAsync(FileInstanceId id, Sha256 hash, CancellationToken cancellationToken);
}
