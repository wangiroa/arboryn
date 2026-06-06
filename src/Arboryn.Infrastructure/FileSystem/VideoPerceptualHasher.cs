using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Matching;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Adapter <see cref="IPerceptualHasher"/> pour les vidéos : extrait quelques keyframes
/// (via <see cref="IVideoKeyframeExtractor"/>), calcule le pHash de chacune, puis les
/// agrège en une empreinte 64 bits par vote majoritaire (<see cref="PerceptualHashAggregator"/>).
/// L'empreinte résultante se range dans la même colonne <c>phash</c> que les images et
/// passe par la même détection perceptuelle.
/// </summary>
public sealed class VideoPerceptualHasher : IPerceptualHasher
{
    /// <summary>Nombre de keyframes échantillonnées — compromis robustesse / coût.</summary>
    public const int MaxKeyframes = 9;

    private readonly IVideoKeyframeExtractor _extractor;

    public VideoPerceptualHasher(IVideoKeyframeExtractor extractor)
        => _extractor = extractor;

    public bool CanHash(MediaCategory category) => category == MediaCategory.Video;

    public async Task<PerceptualHash?> ComputeAsync(FilePath path, CancellationToken cancellationToken)
    {
        var frames = await _extractor
            .ExtractKeyframesAsync(path, MaxKeyframes, cancellationToken).ConfigureAwait(false);

        var hashes = new List<PerceptualHash>(frames.Count);
        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(frame, writable: false);
            if (ImageSharpHashing.Hash(stream) is { } value)
            {
                hashes.Add(new PerceptualHash(value));
            }
        }

        return hashes.Count == 0 ? null : PerceptualHashAggregator.MajorityVote(hashes);
    }
}
