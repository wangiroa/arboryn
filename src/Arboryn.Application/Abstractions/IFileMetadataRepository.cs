using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Stockage des métadonnées extraites par fichier. Une entrée par (instance, clé, source).
/// La fusion par champ (meilleure confiance) est exposée via <see cref="GetFusedAsync"/>.
/// </summary>
public interface IFileMetadataRepository
{
    /// <summary>Insère ou met à jour une entrée (PK : instance + clé + source).</summary>
    Task UpsertAsync(MetadataEntry entry, CancellationToken cancellationToken);

    /// <summary>Toutes les entrées d'une instance, toutes sources confondues.</summary>
    Task<IReadOnlyList<MetadataEntry>> GetForInstanceAsync(FileInstanceId instanceId, CancellationToken cancellationToken);

    /// <summary>
    /// Fusion par clé : meilleure entrée selon (confidence DESC, extracted_at DESC).
    /// Renvoie un dictionnaire clé → entrée fusionnée.
    /// </summary>
    Task<IReadOnlyDictionary<string, MetadataEntry>> GetFusedAsync(
        FileInstanceId instanceId, CancellationToken cancellationToken);

    /// <summary>Supprime toutes les entrées d'une instance (utile au re-scan / clear).</summary>
    Task DeleteForInstanceAsync(FileInstanceId instanceId, CancellationToken cancellationToken);
}

/// <summary>
/// Entrée de métadonnée d'un fichier : couple (clé, valeur) attribué à une source
/// avec un score de confiance.
/// </summary>
public sealed record MetadataEntry(
    FileInstanceId InstanceId,
    string Key,
    string? Value,
    string Source,
    double Confidence,
    DateTime ExtractedAt);

/// <summary>Clés conventionnelles (free string accepté ; ces constantes harmonisent les sources).</summary>
public static class MetadataKeys
{
    public const string Title = "title";
    public const string Subtitle = "subtitle";
    public const string Chapter = "chapter";
    public const string Artist = "artist";
    public const string Album = "album";
    public const string AlbumArtist = "album_artist";
    public const string Year = "year";
    public const string Date = "date";
    public const string Duration = "duration_seconds";
    public const string TrackNumber = "track_number";
    public const string TotalTracks = "total_tracks";
    public const string DiscNumber = "disc_number";
    public const string Genre = "genre";
    public const string Author = "author";
    public const string Publisher = "publisher";
    public const string Language = "language";
    public const string Isbn = "isbn";
    public const string Width = "width";
    public const string Height = "height";
    public const string DateTaken = "date_taken";
    public const string CameraMake = "camera_make";
    public const string CameraModel = "camera_model";
    public const string GpsLatitude = "gps_latitude";
    public const string GpsLongitude = "gps_longitude";
    public const string Resolution = "resolution";
    public const string Codec = "codec";
    public const string ReleaseGroup = "release_group";
    public const string Source = "source_tag";
}

/// <summary>Sources conventionnelles pour tracer la provenance d'une entrée.</summary>
public static class MetadataSources
{
    public const string Filename = "filename";
    public const string Id3 = "id3";
    public const string Exif = "exif";
    public const string PdfInfo = "pdf_info";
    public const string EpubOpf = "epub_opf";
    public const string User = "user";
    public const string Triage = "triage";

    /// <summary>Source d'une métadonnée enrichie en ligne (Inc 8), p.ex. <c>online_openlibrary</c>.</summary>
    public static string Online(string provider) => $"online_{provider}";
}
