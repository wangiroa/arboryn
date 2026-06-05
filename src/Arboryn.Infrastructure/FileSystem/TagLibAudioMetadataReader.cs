using System.Globalization;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Adapter <see cref="IContentMetadataReader"/> basé sur TagLibSharp. Lit les tags
/// ID3/Xiph/Apple sans hypothèse sur le format de fichier (MP3, M4A/M4B, FLAC, OGG…).
/// </summary>
public sealed class TagLibAudioMetadataReader : IContentMetadataReader
{
    public string Source => MetadataSources.Id3;

    public double Confidence => 0.95;

    public bool CanRead(MediaCategory category) => category == MediaCategory.Audiobook;

    public Task<IReadOnlyDictionary<string, string>> ReadAsync(FilePath path, CancellationToken cancellationToken)
        => Task.Run<IReadOnlyDictionary<string, string>>(() => Read(path), cancellationToken);

    private static Dictionary<string, string> Read(FilePath path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        using var file = TagLib.File.Create(path.Value);
        var tag = file.Tag;

        AddIfPresent(result, MetadataKeys.Title, tag.Title);
        AddIfPresent(result, MetadataKeys.Album, tag.Album);
        AddIfPresent(result, MetadataKeys.AlbumArtist, tag.FirstAlbumArtist);
        AddIfPresent(result, MetadataKeys.Genre, tag.FirstGenre);

        var performers = (tag.Performers ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (performers.Count > 0)
        {
            result[MetadataKeys.Artist] = string.Join("; ", performers);
        }

        if (tag.Year > 0)
        {
            result[MetadataKeys.Year] = tag.Year.ToString(CultureInfo.InvariantCulture);
        }

        if (tag.Track > 0)
        {
            result[MetadataKeys.TrackNumber] = tag.Track.ToString(CultureInfo.InvariantCulture);
        }

        if (tag.TrackCount > 0)
        {
            result[MetadataKeys.TotalTracks] = tag.TrackCount.ToString(CultureInfo.InvariantCulture);
        }

        if (tag.Disc > 0)
        {
            result[MetadataKeys.DiscNumber] = tag.Disc.ToString(CultureInfo.InvariantCulture);
        }

        if (file.Properties is { } props && props.Duration.TotalSeconds > 0)
        {
            result[MetadataKeys.Duration] = ((int)props.Duration.TotalSeconds)
                .ToString(CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static void AddIfPresent(IDictionary<string, string> dict, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            dict[key] = value;
        }
    }
}
