using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using CoenM.ImageHash;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using CoenmPerceptualHash = CoenM.ImageHash.HashAlgorithms.PerceptualHash;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Adapter <see cref="IImagePerceptualHasher"/> basé sur CoenM.ImageHash (algorithme
/// pHash) au-dessus de SixLabors.ImageSharp. Décode l'image puis calcule son empreinte
/// perceptuelle 64 bits. Renvoie <c>null</c> si le fichier n'est pas une image décodable.
/// </summary>
public sealed class ImageSharpPerceptualHasher : IImagePerceptualHasher
{
    private readonly IImageHash _algorithm = new CoenmPerceptualHash();

    public Task<PerceptualHash?> ComputeAsync(FilePath path, CancellationToken cancellationToken)
        => Task.Run(() => Compute(path), cancellationToken);

    private PerceptualHash? Compute(FilePath path)
    {
        try
        {
            using var image = Image.Load<Rgba32>(path.Value);
            return new PerceptualHash(_algorithm.Hash(image));
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            // Fichier non décodable comme image : pas d'empreinte perceptuelle.
            return null;
        }
    }
}
