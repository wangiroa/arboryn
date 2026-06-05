namespace Arboryn.Domain.Metadata;

/// <summary>
/// Métadonnées extraites du nom de fichier par <see cref="FilenameCleaner"/> :
/// titre nettoyé (libre de tags techniques) plus champs structurés couramment présents
/// dans les noms de fichiers médias.
/// </summary>
public sealed record FilenameMetadata(
    string CleanTitle,
    int? Year = null,
    string? Resolution = null,
    string? Source = null,
    string? Codec = null,
    string? Audio = null,
    string? Language = null,
    string? ReleaseGroup = null);
