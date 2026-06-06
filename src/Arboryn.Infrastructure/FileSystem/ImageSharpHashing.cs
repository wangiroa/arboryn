using CoenM.ImageHash;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using CoenmPerceptualHash = CoenM.ImageHash.HashAlgorithms.PerceptualHash;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Calcul de l'empreinte perceptuelle (pHash CoenM) d'une image fournie sous forme de flux.
/// Mutualisé entre le hasher d'images et le hasher de vidéos (qui l'applique à chaque keyframe).
/// </summary>
internal static class ImageSharpHashing
{
    // L'algorithme pHash est sans état (fonction pure de l'image) : une instance partagée suffit.
    private static readonly IImageHash Algorithm = new CoenmPerceptualHash();

    /// <summary>Empreinte 64 bits de l'image du flux, ou <c>null</c> si elle n'est pas décodable.</summary>
    public static ulong? Hash(Stream stream)
    {
        try
        {
            using var image = Image.Load<Rgba32>(stream);
            return Algorithm.Hash(image);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return null;
        }
    }
}
