using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using ImageMagick;
using Microsoft.Extensions.Logging;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Rend la miniature de la première page d'un document via Magick.NET : rasterisation PDF
/// (nécessite Ghostscript natif) ou redimensionnement d'image. Se dégrade proprement —
/// renvoie <c>null</c> et avertit une fois si Ghostscript/le runtime natif est absent, sans
/// faire échouer le triage. La sortie est un PNG nommé de façon stable (hash du chemin source)
/// pour être réutilisable d'une préparation à l'autre.
/// </summary>
public sealed class MagickThumbnailRenderer : IDocumentThumbnailRenderer
{
    private static readonly HashSet<string> PdfExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf" };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".webp", ".heic", ".gif",
    };

    private readonly ILogger<MagickThumbnailRenderer> _logger;
    private int _warned;

    public MagickThumbnailRenderer(ILogger<MagickThumbnailRenderer> logger)
        => _logger = logger;

    public bool CanRender(string extension)
    {
        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        return PdfExtensions.Contains(normalized) || ImageExtensions.Contains(normalized);
    }

    public Task<FilePath?> RenderFirstPageAsync(
        FilePath source, string outputDirectory, int maxWidth, CancellationToken cancellationToken)
        => Task.Run(() => Render(source, outputDirectory, maxWidth), cancellationToken);

    private FilePath? Render(FilePath source, string outputDirectory, int maxWidth)
    {
        var outputPath = Path.Combine(outputDirectory, StableName(source.Value) + ".png");

        try
        {
            var isPdf = PdfExtensions.Contains(Path.GetExtension(source.Value));
            using var image = isPdf ? ReadPdfFirstPage(source.Value) : new MagickImage(source.Value);

            if (image.Width > maxWidth)
            {
                image.Resize(new MagickGeometry(maxWidth, 0));
            }

            image.Format = MagickFormat.Png;
            image.Write(outputPath);
            return FilePath.From(outputPath);
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _warned, 1) == 0)
            {
                _logger.LogWarning(
                    ex, "Rendu de miniature indisponible (Ghostscript/runtime natif requis pour les PDF). " +
                        "Le triage continue sans aperçu.");
            }

            return null;
        }
    }

    private static MagickImage ReadPdfFirstPage(string path)
    {
        // Densité modérée : qualité suffisante pour une miniature, rapide. Première page seule.
        var settings = new MagickReadSettings
        {
            Density = new Density(120),
            FrameIndex = 0,
            FrameCount = 1,
        };

        var image = new MagickImage();
        image.Read(path, settings);
        image.BackgroundColor = MagickColors.White;
        image.Alpha(AlphaOption.Remove);
        return image;
    }

    /// <summary>Hash FNV-1a stable du chemin source → nom de fichier de miniature déterministe.</summary>
    private static string StableName(string path)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var c in path.ToLowerInvariant())
        {
            hash ^= c;
            hash *= prime;
        }

        return hash.ToString("x16");
    }
}
