using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Identification de volumes spécifique à Windows (Inc 9). Le numéro de série
/// (VSN NTFS) est lu via <c>GetVolumeInformation</c> ; la nature du support via le
/// type de lecteur. Le marqueur <c>.Arboryn</c> (JSON, fichier caché) posé à la
/// racine porte l'identité stable du volume, indépendante de la lettre de lecteur
/// et reconnaissable sur un autre PC. Tout échec d'I/O se dégrade proprement :
/// numéro de série nul, marqueur non écrit (<c>false</c>), jamais d'exception
/// propagée à l'appelant.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsVolumeIdentifier : IVolumeIdentifier
{
    /// <summary>Nom du marqueur déposé à la racine de chaque volume enrôlé.</summary>
    public const string MarkerFileName = ".Arboryn";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<WindowsVolumeIdentifier> _logger;

    public WindowsVolumeIdentifier(ILogger<WindowsVolumeIdentifier> logger) => _logger = logger;

    public VolumeProbe Probe(FilePath pathOnVolume)
    {
        var rootRaw = Path.GetPathRoot(pathOnVolume.Value);
        if (string.IsNullOrEmpty(rootRaw))
        {
            // Chemin non enraciné : impossible en théorie (FilePath garantit l'absolu),
            // mais on retombe sur le chemin lui-même pour rester robuste.
            rootRaw = pathOnVolume.Value;
        }

        var root = FilePath.From(rootRaw);
        var rootWithSlash = root.Value.EndsWith('\\') ? root.Value : root.Value + "\\";

        // Partage SMB : pas de VSN, identité = \\hôte\partage normalisé.
        if (root.Value.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var fingerprint = root.Value.ToLowerInvariant();
            return new VolumeProbe(root, VolumeKind.Nas, Serial: null, fingerprint, Label: ShareName(root.Value));
        }

        var kind = DetectKind(rootWithSlash);
        var (serial, label) = ReadVolumeInformation(rootWithSlash);
        return new VolumeProbe(root, kind, serial, Fingerprint: null, label);
    }

    public VolumeMarker? ReadMarker(FilePath volumeRoot)
    {
        var path = Path.Combine(volumeRoot.Value, MarkerFileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var dto = JsonSerializer.Deserialize<MarkerDto>(File.ReadAllText(path), JsonOptions);
            if (dto is null || string.IsNullOrWhiteSpace(dto.VolumeId))
            {
                return null;
            }

            return new VolumeMarker(new VolumeId(dto.VolumeId), dto.FirstSeenAt, dto.FriendlyName ?? string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Marqueur .Arboryn illisible à la racine {Root}", volumeRoot.Value);
            return null;
        }
    }

    public bool WriteMarker(FilePath volumeRoot, VolumeMarker marker)
    {
        var path = Path.Combine(volumeRoot.Value, MarkerFileName);
        try
        {
            var dto = new MarkerDto(marker.Id.Value, marker.FirstSeenAt, marker.FriendlyName);

            // Retire l'attribut caché avant réécriture (sinon File.WriteAllText échoue
            // sur un fichier déjà caché), puis le repose.
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions));
            File.SetAttributes(path, FileAttributes.Hidden);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Volume en lecture seule / NAS sans droit d'écriture : l'identité retombe
            // sur le VSN ou l'empreinte SMB en base. Best-effort, pas d'échec bloquant.
            _logger.LogWarning(ex, "Écriture du marqueur .Arboryn impossible à la racine {Root}", volumeRoot.Value);
            return false;
        }
    }

    private static VolumeKind DetectKind(string rootWithSlash)
    {
        try
        {
            var drive = new DriveInfo(rootWithSlash);
            return drive.DriveType switch
            {
                DriveType.Fixed => VolumeKind.Internal,
                DriveType.Removable => VolumeKind.External,
                DriveType.Network => VolumeKind.Nas,
                _ => VolumeKind.Other,
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return VolumeKind.Other;
        }
    }

    /// <summary>Lit le VSN (hex à 8 chiffres) et l'étiquette via Win32, ou <c>(null, null)</c>.</summary>
    private (string? Serial, string? Label) ReadVolumeInformation(string rootWithSlash)
    {
        try
        {
            var labelBuffer = new StringBuilder(261);
            var fsBuffer = new StringBuilder(261);
            if (GetVolumeInformation(rootWithSlash, labelBuffer, labelBuffer.Capacity,
                    out var serialNumber, out _, out _, fsBuffer, fsBuffer.Capacity))
            {
                var serial = serialNumber.ToString("X8", CultureInfo.InvariantCulture);
                var label = labelBuffer.Length > 0 ? labelBuffer.ToString() : null;
                return (serial, label);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _logger.LogWarning(ex, "GetVolumeInformation indisponible pour {Root}", rootWithSlash);
        }

        return (null, null);
    }

    /// <summary>Dernier segment d'un chemin UNC <c>\\hôte\partage</c> → <c>partage</c>.</summary>
    private static string? ShareName(string uncRoot)
    {
        var segments = uncRoot.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 ? segments[^1] : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(
        string rootPathName,
        StringBuilder volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder fileSystemNameBuffer,
        int fileSystemNameSize);

    private sealed record MarkerDto(string VolumeId, DateTime FirstSeenAt, string? FriendlyName);
}
