using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Identifie et enrôle le volume hébergeant un chemin donné (Inc 9). Reconnaît un
/// support déjà connu — par son marqueur <c>.Arboryn</c>, son numéro de série (VSN
/// NTFS) ou son empreinte SMB — quelle que soit sa lettre de lecteur courante, puis
/// migre vers ce volume les instances encore rattachées au volume « default ».
///
/// Ordre de résolution de l'identité :
/// <list type="number">
///   <item>marqueur <c>.Arboryn</c> présent → id qu'il porte (reconnaissance cross-PC) ;</item>
///   <item>sinon, numéro de série connu en base (NTFS rebranché, lettre changée) ;</item>
///   <item>sinon, empreinte <c>\\hôte\partage</c> connue (SMB) ;</item>
///   <item>sinon, nouveau volume (id généré, marqueur écrit).</item>
/// </list>
/// </summary>
public sealed class EnrollVolumeHandler
{
    private readonly IVolumeIdentifier _identifier;
    private readonly IVolumeRepository _volumes;
    private readonly IFileInstanceRepository _instances;
    private readonly ILogger<EnrollVolumeHandler> _logger;

    public EnrollVolumeHandler(
        IVolumeIdentifier identifier,
        IVolumeRepository volumes,
        IFileInstanceRepository instances,
        ILogger<EnrollVolumeHandler> logger)
    {
        _identifier = identifier;
        _volumes = volumes;
        _instances = instances;
        _logger = logger;
    }

    /// <summary>
    /// Enrôle (ou reconnaît) le volume de <paramref name="pathOnVolume"/>.
    /// <paramref name="friendlyName"/> ne s'applique qu'à un nouveau volume ou écrase
    /// le nom courant s'il est fourni ; <c>null</c> conserve/dérive le nom existant.
    /// </summary>
    public async Task<EnrollResult> ExecuteAsync(
        FilePath pathOnVolume, string? friendlyName = null, CancellationToken cancellationToken = default)
    {
        var probe = _identifier.Probe(pathOnVolume);
        var marker = _identifier.ReadMarker(probe.Root);

        var existing = await ResolveExistingAsync(probe, marker, cancellationToken).ConfigureAwait(false);
        var isNew = existing is null;

        var id = existing?.Id ?? marker?.Id ?? VolumeId.New();
        var firstSeen = marker?.FirstSeenAt ?? existing?.LastSeenAt ?? DateTime.UtcNow;
        var name = friendlyName
                   ?? existing?.Name
                   ?? marker?.FriendlyName
                   ?? DeriveName(probe);

        var record = new VolumeRecord(id, name, probe.Kind, VolumeStatus.Online)
        {
            Serial = probe.Serial,
            Fingerprint = probe.Fingerprint,
            Label = probe.Label,
            MountPoint = probe.Root.Value,
            LastSeenAt = DateTime.UtcNow,
            // Préserve les champs gérés ailleurs (scan / réplication).
            LastUsn = existing?.LastUsn,
            LastScanAt = existing?.LastScanAt,
            ReplicationScopeId = existing?.ReplicationScopeId,
        };
        await _volumes.UpsertAsync(record, cancellationToken).ConfigureAwait(false);

        // (Ré)écrit le marqueur si absent ou désynchronisé, pour garantir la
        // reconnaissance ultérieure (best-effort : volume en lecture seule toléré).
        var markerWritten = true;
        if (marker is null || marker.Id != id || !string.Equals(marker.FriendlyName, name, StringComparison.Ordinal))
        {
            markerWritten = _identifier.WriteMarker(probe.Root, new VolumeMarker(id, firstSeen, name));
        }

        // Migre les instances « default » situées sous ce volume vers son id réel.
        var migrated = await _instances
            .ReassignDefaultUnderRootAsync(probe.Root, id, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Volume {Name} ({Id}) {Action} sous {Root} ; {Migrated} instance(s) migrée(s) depuis « default »",
            name, id, isNew ? "enrôlé" : "reconnu", probe.Root.Value, migrated);

        return new EnrollResult(record, isNew, migrated, markerWritten);
    }

    private async Task<VolumeRecord?> ResolveExistingAsync(
        VolumeProbe probe, VolumeMarker? marker, CancellationToken cancellationToken)
    {
        if (marker is not null)
        {
            var byMarker = await _volumes.GetAsync(marker.Id, cancellationToken).ConfigureAwait(false);
            if (byMarker is not null)
            {
                return byMarker;
            }
        }

        if (probe.Serial is not null)
        {
            var bySerial = await _volumes.FindBySerialAsync(probe.Serial, cancellationToken).ConfigureAwait(false);
            if (bySerial is not null)
            {
                return bySerial;
            }
        }

        if (probe.Fingerprint is not null)
        {
            return await _volumes.FindByFingerprintAsync(probe.Fingerprint, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static string DeriveName(VolumeProbe probe)
    {
        if (!string.IsNullOrWhiteSpace(probe.Label))
        {
            return probe.Label!;
        }

        // À défaut d'étiquette : racine de montage (« E:\ », « \\hôte\partage »).
        return string.IsNullOrWhiteSpace(probe.Root.Value) ? "Volume" : probe.Root.Value;
    }
}

/// <summary>Résultat d'un enrôlement de volume.</summary>
/// <param name="Volume">État persistant du volume après enrôlement.</param>
/// <param name="IsNewlyEnrolled"><c>true</c> si le volume n'était pas connu en base.</param>
/// <param name="MigratedInstances">Nombre d'instances réaffectées depuis « default ».</param>
/// <param name="MarkerWritten"><c>false</c> si le marqueur n'a pas pu être écrit (lecture seule).</param>
public sealed record EnrollResult(
    VolumeRecord Volume,
    bool IsNewlyEnrolled,
    int MigratedInstances,
    bool MarkerWritten);
