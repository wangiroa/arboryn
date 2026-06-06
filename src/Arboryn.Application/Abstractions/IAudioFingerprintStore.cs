using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Stockage des empreintes acoustiques par FileInstance (colonne
/// <c>file_instances.chromaprint</c>). L'empreinte est invalidée au re-scan si le fichier change.
/// </summary>
public interface IAudioFingerprintStore
{
    /// <summary>
    /// Instances actives encore dépourvues d'empreinte acoustique (à calculer),
    /// optionnellement limitées au sous-arbre <paramref name="underRoot"/>.
    /// </summary>
    Task<IReadOnlyList<FileInstanceRecord>> GetWithoutFingerprintAsync(
        VolumeId volumeId, FilePath? underRoot, CancellationToken cancellationToken);

    Task SetAsync(FileInstanceId id, AudioFingerprint fingerprint, CancellationToken cancellationToken);

    /// <summary>Instances actives porteuses d'une empreinte, pour la détection acoustique.</summary>
    Task<IReadOnlyList<AudioFingerprintedInstance>> GetFingerprintedAsync(
        VolumeId volumeId, FilePath? underRoot, CancellationToken cancellationToken);
}

/// <summary>Une FileInstance et son empreinte acoustique.</summary>
public sealed record AudioFingerprintedInstance(FileInstanceRecord Instance, AudioFingerprint Fingerprint);
