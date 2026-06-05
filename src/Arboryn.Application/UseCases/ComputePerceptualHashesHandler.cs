using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Metadata;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Calcule et persiste l'empreinte perceptuelle des images d'un volume encore
/// dépourvues d'empreinte (Inc 5). Opération coûteuse, déclenchée à la demande
/// (hors du scan initial). Les fichiers non décodables sont ignorés silencieusement.
/// </summary>
public sealed class ComputePerceptualHashesHandler
{
    private readonly IPerceptualHashStore _store;
    private readonly IImagePerceptualHasher _hasher;
    private readonly ILogger<ComputePerceptualHashesHandler> _logger;

    public ComputePerceptualHashesHandler(
        IPerceptualHashStore store,
        IImagePerceptualHasher hasher,
        ILogger<ComputePerceptualHashesHandler> logger)
    {
        _store = store;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(
        VolumeId volumeId,
        FilePath? underRoot = null,
        IProgress<PerceptualHashProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pending = await _store
            .GetWithoutPerceptualHashAsync(volumeId, underRoot, cancellationToken).ConfigureAwait(false);

        var hashed = 0;
        var seen = 0;
        foreach (var instance in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Seules les images portent une empreinte perceptuelle à ce stade (Inc 5).
            if (MediaClassifier.FromExtension(instance.Path.Extension) != MediaCategory.Photo)
            {
                continue;
            }

            try
            {
                var hash = await _hasher.ComputeAsync(instance.Path, cancellationToken).ConfigureAwait(false);
                if (hash is { } value)
                {
                    await _store.SetAsync(instance.Id, value, cancellationToken).ConfigureAwait(false);
                    hashed++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Empreinte perceptuelle impossible pour {Path}", instance.Path);
            }

            if (++seen % ProgressEvery == 0)
            {
                progress?.Report(new PerceptualHashProgress(seen, hashed));
            }
        }

        progress?.Report(new PerceptualHashProgress(seen, hashed));
        _logger.LogInformation("Empreintes perceptuelles calculées : {Hashed} image(s) sur le volume {Volume}", hashed, volumeId);
        return hashed;
    }

    private const int ProgressEvery = 50;
}

/// <summary>Avancement du calcul des empreintes perceptuelles.</summary>
public sealed record PerceptualHashProgress(int ImagesSeen, int Hashed);
