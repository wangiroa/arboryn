using System.Diagnostics;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Adapter <see cref="IVideoKeyframeExtractor"/> basé sur ffmpeg (suite FFmpeg, embarquée
/// dans <c>tools/</c> ou présente dans le PATH). Décode uniquement les keyframes
/// (<c>-skip_frame nokey</c>), les redimensionne et les écrit en PNG dans un dossier
/// temporaire, puis renvoie leurs octets.
///
/// Si ffmpeg est introuvable ou échoue, renvoie une liste vide (mode dégradé).
/// </summary>
public sealed class FfmpegKeyframeExtractor : IVideoKeyframeExtractor
{
    public const string FfmpegPathEnvironmentVariable = "ARBORYN_FFMPEG_PATH";

    private const string ExecutableName = "ffmpeg.exe";

    private readonly ExternalToolResolver _resolver;
    private readonly ILogger<FfmpegKeyframeExtractor> _logger;
    private bool _missingWarned;

    public FfmpegKeyframeExtractor(ExternalToolResolver resolver, ILogger<FfmpegKeyframeExtractor> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<IReadOnlyList<byte[]>> ExtractKeyframesAsync(
        FilePath videoPath, int maxFrames, CancellationToken cancellationToken)
    {
        var configured = Environment.GetEnvironmentVariable(FfmpegPathEnvironmentVariable);
        var executable = _resolver.Resolve(ExecutableName, configured);
        if (executable is null)
        {
            if (!_missingWarned)
            {
                _missingWarned = true;
                _logger.LogWarning(
                    "ffmpeg introuvable (ni dans tools/, ni dans le PATH, ni via {EnvVar}) : empreintes vidéo désactivées.",
                    FfmpegPathEnvironmentVariable);
            }

            return Array.Empty<byte[]>();
        }

        var workDir = Path.Combine(Path.GetTempPath(), "arboryn-kf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                ArgumentList =
                {
                    "-hide_banner", "-loglevel", "error",
                    "-skip_frame", "nokey",            // ne décode que les keyframes (rapide)
                    "-i", videoPath.Value,
                    "-an", "-sn",                       // ignore audio / sous-titres
                    "-vsync", "vfr",
                    "-frames:v", maxFrames.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-vf", "scale=160:-1",              // miniature : suffit au pHash, accélère
                    Path.Combine(workDir, "kf_%03d.png"),
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Array.Empty<byte[]>();
            }

            await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            // Lit toutes les keyframes produites (même si ffmpeg a fini en erreur partielle).
            var frames = new List<byte[]>();
            foreach (var file in Directory.EnumerateFiles(workDir, "kf_*.png").OrderBy(f => f, StringComparer.Ordinal))
            {
                frames.Add(await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false));
            }

            return frames;
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (IOException)
            {
                // Nettoyage best-effort.
            }
        }
    }
}
