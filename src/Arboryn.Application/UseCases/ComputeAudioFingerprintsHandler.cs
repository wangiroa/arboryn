using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Metadata;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Calcule et persiste l'empreinte acoustique (Chromaprint) des fichiers audio d'un
/// volume encore dépourvus d'empreinte (Inc 5). Opération coûteuse (décodage audio via
/// fpcalc), déclenchée à la demande. Les fichiers non décodables — ou si fpcalc est
/// indisponible — sont ignorés silencieusement.
/// </summary>
public sealed class ComputeAudioFingerprintsHandler
{
    private readonly IAudioFingerprintStore _store;
    private readonly IAudioFingerprinter _fingerprinter;
    private readonly ILogger<ComputeAudioFingerprintsHandler> _logger;

    public ComputeAudioFingerprintsHandler(
        IAudioFingerprintStore store,
        IAudioFingerprinter fingerprinter,
        ILogger<ComputeAudioFingerprintsHandler> logger)
    {
        _store = store;
        _fingerprinter = fingerprinter;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(
        VolumeId volumeId,
        FilePath? underRoot = null,
        IProgress<AudioFingerprintProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pending = await _store
            .GetWithoutFingerprintAsync(volumeId, underRoot, cancellationToken).ConfigureAwait(false);

        var fingerprinted = 0;
        var seen = 0;
        foreach (var instance in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Seuls les fichiers audio portent une empreinte acoustique.
            if (MediaClassifier.FromExtension(instance.Path.Extension) != MediaCategory.Audiobook)
            {
                continue;
            }

            try
            {
                var fingerprint = await _fingerprinter.ComputeAsync(instance.Path, cancellationToken).ConfigureAwait(false);
                if (fingerprint is not null)
                {
                    await _store.SetAsync(instance.Id, fingerprint, cancellationToken).ConfigureAwait(false);
                    fingerprinted++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Empreinte acoustique impossible pour {Path}", instance.Path);
            }

            if (++seen % ProgressEvery == 0)
            {
                progress?.Report(new AudioFingerprintProgress(seen, fingerprinted));
            }
        }

        progress?.Report(new AudioFingerprintProgress(seen, fingerprinted));
        _logger.LogInformation(
            "Empreintes acoustiques calculées : {Count} fichier(s) audio sur le volume {Volume}",
            fingerprinted, volumeId);
        return fingerprinted;
    }

    private const int ProgressEvery = 20;
}

/// <summary>Avancement du calcul des empreintes acoustiques.</summary>
public sealed record AudioFingerprintProgress(int AudioSeen, int Fingerprinted);
