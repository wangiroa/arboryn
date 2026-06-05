using System.Globalization;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using ExtractorReader = MetadataExtractor.ImageMetadataReader;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Adapter <see cref="IContentMetadataReader"/> basé sur MetadataExtractor. Lit les
/// dimensions d'une image (tous formats) et, si présents, les champs EXIF utiles :
/// date de prise de vue, marque/modèle d'appareil, coordonnées GPS.
/// </summary>
public sealed class ImageMetadataReader : IContentMetadataReader
{
    public string Source => MetadataSources.Exif;

    public double Confidence => 0.9;

    public bool CanRead(MediaCategory category) => category == MediaCategory.Photo;

    public Task<IReadOnlyDictionary<string, string>> ReadAsync(FilePath path, CancellationToken cancellationToken)
        => Task.Run<IReadOnlyDictionary<string, string>>(() => Read(path), cancellationToken);

    private static Dictionary<string, string> Read(FilePath path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        var directories = ExtractorReader.ReadMetadata(path.Value);

        ExtractDimensions(directories, result);

        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        if (ifd0 is not null)
        {
            AddIfPresent(result, MetadataKeys.CameraMake, ifd0.GetDescription(ExifDirectoryBase.TagMake));
            AddIfPresent(result, MetadataKeys.CameraModel, ifd0.GetDescription(ExifDirectoryBase.TagModel));
        }

        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        if (subIfd is not null
            && subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken))
        {
            result[MetadataKeys.DateTaken] = taken.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }

        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        var location = gps?.GetGeoLocation();
        if (location is not null && !location.IsZero)
        {
            result[MetadataKeys.GpsLatitude] = location.Latitude.ToString("0.######", CultureInfo.InvariantCulture);
            result[MetadataKeys.GpsLongitude] = location.Longitude.ToString("0.######", CultureInfo.InvariantCulture);
        }

        return result;
    }

    /// <summary>
    /// Récupère largeur/hauteur quel que soit le format. Les directories portent des
    /// tags nommés « Image Width » / « Image Height » (ou « Exif Image Width/Height ») ;
    /// on prend la première valeur entière plausible trouvée.
    /// </summary>
    private static void ExtractDimensions(
        IReadOnlyList<MetadataExtractor.Directory> directories, IDictionary<string, string> result)
    {
        foreach (var dir in directories)
        {
            foreach (var tag in dir.Tags)
            {
                if (!result.ContainsKey(MetadataKeys.Width)
                    && tag.Name.EndsWith("Image Width", StringComparison.OrdinalIgnoreCase)
                    && TryParseLeadingInt(dir.GetDescription(tag.Type), out var w))
                {
                    result[MetadataKeys.Width] = w.ToString(CultureInfo.InvariantCulture);
                }
                else if (!result.ContainsKey(MetadataKeys.Height)
                    && tag.Name.EndsWith("Image Height", StringComparison.OrdinalIgnoreCase)
                    && TryParseLeadingInt(dir.GetDescription(tag.Type), out var h))
                {
                    result[MetadataKeys.Height] = h.ToString(CultureInfo.InvariantCulture);
                }
            }

            if (result.ContainsKey(MetadataKeys.Width) && result.ContainsKey(MetadataKeys.Height))
            {
                return;
            }
        }
    }

    private static bool TryParseLeadingInt(string? description, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(description))
        {
            return false;
        }

        var end = 0;
        while (end < description.Length && char.IsDigit(description[end]))
        {
            end++;
        }

        return end > 0 && int.TryParse(description[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static void AddIfPresent(IDictionary<string, string> dict, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            dict[key] = value.Trim();
        }
    }
}
