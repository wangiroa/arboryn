using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Extrait quelques keyframes d'une vidéo sous forme d'images encodées (PNG), pour en
/// calculer l'empreinte perceptuelle. Renvoie une liste vide si la vidéo est illisible ou
/// si l'outil externe (ffmpeg) est indisponible.
/// </summary>
public interface IVideoKeyframeExtractor
{
    Task<IReadOnlyList<byte[]>> ExtractKeyframesAsync(
        FilePath videoPath, int maxFrames, CancellationToken cancellationToken);
}
