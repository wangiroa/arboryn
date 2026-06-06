using System.Diagnostics;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Adapter <see cref="IAudioFingerprinter"/> basé sur l'outil Chromaprint <c>fpcalc</c>
/// (binaire embarqué dans <c>tools/</c> ou présent dans le PATH). Lance
/// <c>fpcalc -raw -json &lt;fichier&gt;</c> et lit l'empreinte brute.
///
/// Si fpcalc est introuvable, renvoie <c>null</c> en journalisant un avertissement unique :
/// la fonctionnalité acoustique est alors simplement inactive (mode dégradé).
/// </summary>
public sealed class FpcalcAudioFingerprinter : IAudioFingerprinter
{
    /// <summary>Chemin fpcalc explicite (prioritaire) ; sinon résolution tools/ puis PATH.</summary>
    public const string FpcalcPathEnvironmentVariable = "ARBORYN_FPCALC_PATH";

    private const string ExecutableName = "fpcalc.exe";

    private readonly ExternalToolResolver _resolver;
    private readonly ILogger<FpcalcAudioFingerprinter> _logger;
    private bool _missingWarned;

    public FpcalcAudioFingerprinter(ExternalToolResolver resolver, ILogger<FpcalcAudioFingerprinter> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<AudioFingerprint?> ComputeAsync(FilePath path, CancellationToken cancellationToken)
    {
        var configured = Environment.GetEnvironmentVariable(FpcalcPathEnvironmentVariable);
        var executable = _resolver.Resolve(ExecutableName, configured);
        if (executable is null)
        {
            if (!_missingWarned)
            {
                _missingWarned = true;
                _logger.LogWarning(
                    "fpcalc introuvable (ni dans tools/, ni dans le PATH, ni via {EnvVar}) : empreintes acoustiques désactivées.",
                    FpcalcPathEnvironmentVariable);
            }

            return null;
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            ArgumentList = { "-raw", "-json", path.Value },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            _logger.LogDebug("fpcalc a échoué (code {Code}) pour {Path}", process.ExitCode, path);
            return null;
        }

        return FpcalcOutputParser.Parse(stdout);
    }
}
