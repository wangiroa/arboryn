using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Stockage des empreintes perceptuelles par FileInstance (colonne
/// <c>file_instances.phash</c>). L'empreinte est invalidée au re-scan si le fichier change.
/// </summary>
public interface IPerceptualHashStore
{
    /// <summary>
    /// Instances actives encore dépourvues d'empreinte perceptuelle (à calculer),
    /// optionnellement limitées au sous-arbre <paramref name="underRoot"/>.
    /// </summary>
    Task<IReadOnlyList<FileInstanceRecord>> GetWithoutPerceptualHashAsync(
        VolumeId volumeId, FilePath? underRoot, CancellationToken cancellationToken);

    Task SetAsync(FileInstanceId id, PerceptualHash hash, CancellationToken cancellationToken);

    /// <summary>Instances actives porteuses d'une empreinte, pour la détection perceptuelle.</summary>
    Task<IReadOnlyList<PerceptualHashedInstance>> GetHashedAsync(
        VolumeId volumeId, FilePath? underRoot, CancellationToken cancellationToken);
}

/// <summary>Une FileInstance et son empreinte perceptuelle.</summary>
public sealed record PerceptualHashedInstance(FileInstanceRecord Instance, PerceptualHash Hash);
