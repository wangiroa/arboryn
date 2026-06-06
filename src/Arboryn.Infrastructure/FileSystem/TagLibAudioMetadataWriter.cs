using System.Globalization;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Adapter <see cref="IContentMetadataWriter"/> pour l'audio, basé sur TagLibSharp. Écrit
/// titre / album / artiste / artiste-album / année / piste dans les tags du fichier, et
/// renvoie les valeurs précédentes pour l'annulation.
/// </summary>
public sealed class TagLibAudioMetadataWriter : IContentMetadataWriter
{
    public bool CanWrite(MediaCategory category) => category == MediaCategory.Audiobook;

    public Task<IReadOnlyDictionary<string, string?>> WriteAsync(
        FilePath path, IReadOnlyDictionary<string, string?> fields, CancellationToken cancellationToken)
        => Task.Run<IReadOnlyDictionary<string, string?>>(() => Write(path, fields), cancellationToken);

    private static Dictionary<string, string?> Write(FilePath path, IReadOnlyDictionary<string, string?> fields)
    {
        var previous = new Dictionary<string, string?>(StringComparer.Ordinal);

        using var file = TagLib.File.Create(path.Value);
        var tag = file.Tag;
        var changed = false;

        if (fields.TryGetValue(MetadataKeys.Title, out var title))
        {
            previous[MetadataKeys.Title] = tag.Title;
            tag.Title = Normalize(title);
            changed = true;
        }

        if (fields.TryGetValue(MetadataKeys.Album, out var album))
        {
            previous[MetadataKeys.Album] = tag.Album;
            tag.Album = Normalize(album);
            changed = true;
        }

        if (fields.TryGetValue(MetadataKeys.AlbumArtist, out var albumArtist))
        {
            previous[MetadataKeys.AlbumArtist] = tag.FirstAlbumArtist;
            tag.AlbumArtists = ToArray(albumArtist);
            changed = true;
        }

        if (fields.TryGetValue(MetadataKeys.Artist, out var artist))
        {
            previous[MetadataKeys.Artist] = JoinPerformers(tag.Performers);
            tag.Performers = artist is null
                ? Array.Empty<string>()
                : artist.Split("; ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            changed = true;
        }

        if (fields.TryGetValue(MetadataKeys.Year, out var year))
        {
            previous[MetadataKeys.Year] = tag.Year > 0 ? tag.Year.ToString(CultureInfo.InvariantCulture) : null;
            tag.Year = uint.TryParse(year, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ? y : 0;
            changed = true;
        }

        if (fields.TryGetValue(MetadataKeys.TrackNumber, out var track))
        {
            previous[MetadataKeys.TrackNumber] = tag.Track > 0 ? tag.Track.ToString(CultureInfo.InvariantCulture) : null;
            tag.Track = uint.TryParse(track, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : 0;
            changed = true;
        }

        if (changed)
        {
            file.Save();
        }

        return previous;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string[] ToArray(string? value)
        => string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : new[] { value };

    private static string? JoinPerformers(string[]? performers)
    {
        if (performers is null || performers.Length == 0)
        {
            return null;
        }

        var present = performers.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return present.Length == 0 ? null : string.Join("; ", present);
    }
}
