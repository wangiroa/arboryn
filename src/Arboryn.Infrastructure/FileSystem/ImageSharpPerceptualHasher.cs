using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Adapter <see cref="IPerceptualHasher"/> pour les images, basé sur CoenM.ImageHash
/// (algorithme pHash) au-dessus de SixLabors.ImageSharp. Décode l'image puis calcule son
/// empreinte perceptuelle 64 bits. Renvoie <c>null</c> si le fichier n'est pas une image décodable.
/// </summary>
public sealed class ImageSharpPerceptualHasher : IPerceptualHasher
{
    public bool CanHash(MediaCategory category) => category == MediaCategory.Photo;

    public Task<PerceptualHash?> ComputeAsync(FilePath path, CancellationToken cancellationToken)
        => Task.Run(() => Compute(path), cancellationToken);

    private static PerceptualHash? Compute(FilePath path)
    {
        using var stream = File.OpenRead(path.Value);
        return ImageSharpHashing.Hash(stream) is { } value ? new PerceptualHash(value) : null;
    }
}
