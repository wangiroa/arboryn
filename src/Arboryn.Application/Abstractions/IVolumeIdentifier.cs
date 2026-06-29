using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Identifie le volume physique qui héberge un chemin donné (Inc 9) et gère le
/// marqueur <c>.Arboryn</c> posé à sa racine. L'implémentation est spécifique à
/// Windows (numéro de série NTFS via Win32, type de lecteur). Elle se dégrade
/// proprement : un <see cref="VolumeProbe.Serial"/> nul n'empêche pas l'enrôlement,
/// qui retombe sur l'empreinte SMB ou sur le marqueur.
/// </summary>
public interface IVolumeIdentifier
{
    /// <summary>
    /// Sonde le volume hébergeant <paramref name="pathOnVolume"/> : racine, nature,
    /// numéro de série, empreinte SMB et étiquette. Ne lit pas le marqueur.
    /// </summary>
    VolumeProbe Probe(FilePath pathOnVolume);

    /// <summary>Lit le marqueur <c>.Arboryn</c> à la racine, ou <c>null</c> s'il est absent/illisible.</summary>
    VolumeMarker? ReadMarker(FilePath volumeRoot);

    /// <summary>
    /// Écrit (ou réécrit) le marqueur <c>.Arboryn</c> à la racine. Best-effort :
    /// un volume en lecture seule renvoie <c>false</c> sans lever d'exception.
    /// </summary>
    bool WriteMarker(FilePath volumeRoot, VolumeMarker marker);
}

/// <summary>
/// Résultat d'une sonde de volume. <see cref="Root"/> est la racine du volume
/// (racine de lecteur <c>X:\</c> ou racine de partage <c>\\hôte\partage</c>).
/// </summary>
public sealed record VolumeProbe(
    FilePath Root,
    VolumeKind Kind,
    string? Serial,
    string? Fingerprint,
    string? Label);

/// <summary>
/// Contenu du marqueur <c>.Arboryn</c> : porte l'identité stable du volume,
/// indépendante de la lettre de lecteur, et reconnaissable sur un autre PC.
/// </summary>
public sealed record VolumeMarker(
    VolumeId Id,
    DateTime FirstSeenAt,
    string FriendlyName);
