using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Lecture du USN Journal NTFS via Win32 (Inc 9), pour un re-scan incrémental rapide.
/// Entièrement best-effort : toute condition non satisfaite (volume non-NTFS, pas de lettre
/// de lecteur, journal absent ou réinitialisé, accès refusé faute d'élévation, jeu de
/// changements trop volumineux) renvoie <c>null</c> et laisse le <see cref="Application.UseCases.RescanVolumeHandler"/>
/// retomber sur un parcours mtime complet. Aucune exception n'est propagée.
///
/// Ouvrir le volume (<c>\\.\X:</c>) requiert en général une élévation : sans elle, l'ouverture
/// échoue proprement et le repli s'applique.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUsnJournalReader : IUsnJournalReader
{
    private const uint FSCTL_QUERY_USN_JOURNAL = 0x000900f4;
    private const uint FSCTL_READ_USN_JOURNAL = 0x000900bb;

    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_READ_ATTRIBUTES = 0x80;

    private const uint USN_REASON_FILE_DELETE = 0x00000200;
    private const uint USN_REASON_RENAME_OLD_NAME = 0x00001000;
    private const uint USN_REASON_RENAME_NEW_NAME = 0x00002000;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    // Au-delà, le delta n'a plus d'avantage sur un parcours complet : on renonce au journal.
    private const int MaxChanges = 200_000;
    private const int ReadBufferSize = 64 * 1024;

    private readonly ILogger<WindowsUsnJournalReader> _logger;

    public WindowsUsnJournalReader(ILogger<WindowsUsnJournalReader> logger) => _logger = logger;

    public Task<UsnChangeSet?> TryReadChangesAsync(VolumeRecord volume, FilePath root, CancellationToken cancellationToken)
        => Task.Run(() => TryReadChanges(volume, root), cancellationToken);

    public Task<long?> TryGetCurrentPositionAsync(VolumeRecord volume, CancellationToken cancellationToken)
        => Task.Run(() => TryGetCurrentPosition(volume), cancellationToken);

    private UsnChangeSet? TryReadChanges(VolumeRecord volume, FilePath root)
    {
        // Pas de référence USN → pas de delta possible (le parcours complet posera la référence).
        if (volume.LastUsn is not { } sinceUsn)
        {
            return null;
        }

        var device = DevicePath(volume);
        if (device is null)
        {
            return null;
        }

        try
        {
            using var handle = OpenVolume(device);
            if (handle.IsInvalid)
            {
                return null;
            }

            if (!QueryJournal(handle, out var journal))
            {
                return null;
            }

            // Journal réinitialisé / position trop ancienne (rotation) : delta non fiable.
            if (sinceUsn < journal.FirstUsn || sinceUsn > journal.NextUsn)
            {
                return null;
            }

            var prefix = RootPrefix(root);
            var parentPaths = new Dictionary<ulong, string?>();
            var changes = new List<UsnChange>();

            var inBuffer = new READ_USN_JOURNAL_DATA_V0
            {
                StartUsn = sinceUsn,
                ReasonMask = 0xFFFFFFFF,
                ReturnOnlyOnClose = 1,
                Timeout = 0,
                BytesToWaitFor = 0,
                UsnJournalID = journal.UsnJournalID,
            };

            if (!ReadAllRecords(handle, inBuffer, journal.NextUsn, prefix, parentPaths, changes))
            {
                return null; // trop de changements → parcours complet
            }

            return new UsnChangeSet(changes, journal.NextUsn);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Lecture USN indisponible pour {Volume} — repli sur parcours complet", volume.Name);
            return null;
        }
    }

    private long? TryGetCurrentPosition(VolumeRecord volume)
    {
        var device = DevicePath(volume);
        if (device is null)
        {
            return null;
        }

        try
        {
            using var handle = OpenVolume(device);
            if (handle.IsInvalid || !QueryJournal(handle, out var journal))
            {
                return null;
            }

            return journal.NextUsn;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Position USN indisponible pour {Volume}", volume.Name);
            return null;
        }
    }

    /// <summary>Boucle de lecture du journal ; remplit <paramref name="changes"/>. Renvoie false si dépassement.</summary>
    private bool ReadAllRecords(
        SafeFileHandle handle, READ_USN_JOURNAL_DATA_V0 inBuffer, long stopUsn,
        string prefix, Dictionary<ulong, string?> parentPaths, List<UsnChange> changes)
    {
        var outBuffer = Marshal.AllocHGlobal(ReadBufferSize);
        var inPtr = Marshal.AllocHGlobal(Marshal.SizeOf<READ_USN_JOURNAL_DATA_V0>());
        try
        {
            var managed = new byte[ReadBufferSize];
            while (true)
            {
                Marshal.StructureToPtr(inBuffer, inPtr, false);
                if (!DeviceIoControl(handle, FSCTL_READ_USN_JOURNAL, inPtr, Marshal.SizeOf<READ_USN_JOURNAL_DATA_V0>(),
                        outBuffer, ReadBufferSize, out var bytesReturned, IntPtr.Zero))
                {
                    return false;
                }

                // Les 8 premiers octets portent la prochaine StartUsn ; les enregistrements suivent.
                if (bytesReturned <= sizeof(long))
                {
                    return true; // plus rien à lire
                }

                Marshal.Copy(outBuffer, managed, 0, bytesReturned);
                var nextStartUsn = BitConverter.ToInt64(managed, 0);

                var offset = sizeof(long);
                while (offset < bytesReturned)
                {
                    var recordLength = (int)BitConverter.ToUInt32(managed, offset);
                    if (recordLength <= 0 || offset + recordLength > bytesReturned)
                    {
                        break;
                    }

                    ParseRecord(handle, managed, offset, prefix, parentPaths, changes);
                    if (changes.Count > MaxChanges)
                    {
                        return false;
                    }

                    offset += recordLength;
                }

                inBuffer.StartUsn = nextStartUsn;
                if (nextStartUsn >= stopUsn)
                {
                    return true;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(outBuffer);
            Marshal.FreeHGlobal(inPtr);
        }
    }

    /// <summary>Décode un USN_RECORD_V2 et ajoute le changement s'il concerne un fichier sous la racine.</summary>
    private void ParseRecord(
        SafeFileHandle handle, byte[] buffer, int offset,
        string prefix, Dictionary<ulong, string?> parentPaths, List<UsnChange> changes)
    {
        var majorVersion = BitConverter.ToUInt16(buffer, offset + 4);
        if (majorVersion != 2)
        {
            return; // READ_USN_JOURNAL_DATA_V0 ne devrait produire que du V2
        }

        var parentFrn = BitConverter.ToUInt64(buffer, offset + 16);
        var reason = BitConverter.ToUInt32(buffer, offset + 40);
        var attributes = BitConverter.ToUInt32(buffer, offset + 52);
        var fileNameLength = BitConverter.ToUInt16(buffer, offset + 56);
        var fileNameOffset = BitConverter.ToUInt16(buffer, offset + 58);

        if ((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
        {
            return; // on ne suit que les fichiers
        }

        var fileName = Encoding.Unicode.GetString(buffer, offset + fileNameOffset, fileNameLength);
        var parentPath = ResolveParent(handle, parentFrn, parentPaths);
        if (parentPath is null)
        {
            return; // parent introuvable (supprimé aussi) → on ne peut pas localiser
        }

        var fullPath = System.IO.Path.Combine(parentPath, fileName);
        if (!fullPath.ToLowerInvariant().StartsWith(prefix, StringComparison.Ordinal))
        {
            return; // hors racine demandée
        }

        // Suppression ou ancien nom d'un renommage (le fichier quitte ce chemin).
        var deleted = (reason & (USN_REASON_FILE_DELETE | USN_REASON_RENAME_OLD_NAME)) != 0
                      && (reason & USN_REASON_RENAME_NEW_NAME) == 0;

        try
        {
            changes.Add(new UsnChange(FilePath.From(fullPath), deleted));
        }
        catch (ArgumentException)
        {
            // Chemin non enraciné / invalide : ignoré.
        }
    }

    private string? ResolveParent(SafeFileHandle volumeHandle, ulong parentFrn, Dictionary<ulong, string?> cache)
    {
        if (cache.TryGetValue(parentFrn, out var cached))
        {
            return cached;
        }

        string? path = null;
        var descriptor = new FILE_ID_DESCRIPTOR
        {
            dwSize = (uint)Marshal.SizeOf<FILE_ID_DESCRIPTOR>(),
            Type = 0, // FileIdType
            FileId = unchecked((long)parentFrn),
        };

        using var parent = OpenFileById(volumeHandle, in descriptor, FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, FILE_FLAG_BACKUP_SEMANTICS);
        if (!parent.IsInvalid)
        {
            var builder = new StringBuilder(1024);
            var length = GetFinalPathNameByHandle(parent, builder, (uint)builder.Capacity, 0);
            if (length > 0)
            {
                path = StripExtendedPrefix(builder.ToString());
            }
        }

        cache[parentFrn] = path;
        return path;
    }

    private static bool QueryJournal(SafeFileHandle handle, out USN_JOURNAL_DATA_V0 journal)
    {
        var size = Marshal.SizeOf<USN_JOURNAL_DATA_V0>();
        var outBuffer = Marshal.AllocHGlobal(size);
        try
        {
            if (DeviceIoControl(handle, FSCTL_QUERY_USN_JOURNAL, IntPtr.Zero, 0,
                    outBuffer, size, out _, IntPtr.Zero))
            {
                journal = Marshal.PtrToStructure<USN_JOURNAL_DATA_V0>(outBuffer);
                return true;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(outBuffer);
        }

        journal = default;
        return false;
    }

    private static SafeFileHandle OpenVolume(string devicePath) => CreateFile(
        devicePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
        IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

    /// <summary>Chemin device <c>\\.\X:</c> à partir d'un point de montage à lettre de lecteur, ou null.</summary>
    private static string? DevicePath(VolumeRecord volume)
    {
        var mount = volume.MountPoint;
        if (string.IsNullOrWhiteSpace(mount) || mount!.Length < 2 || mount[1] != ':')
        {
            return null; // UNC / non-lettré : USN NTFS local uniquement
        }

        return $@"\\.\{char.ToUpperInvariant(mount[0])}:";
    }

    private static string RootPrefix(FilePath root)
        => (root.Value.EndsWith('\\') ? root.Value : root.Value + "\\").ToLowerInvariant();

    private static string StripExtendedPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
        {
            return @"\\" + path[8..];
        }

        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle OpenFileById(
        SafeFileHandle hVolumeHint, in FILE_ID_DESCRIPTOR lpFileId, uint dwDesiredAccess,
        uint dwShareMode, IntPtr lpSecurityAttributes, uint dwFlagsAndAttributes);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct READ_USN_JOURNAL_DATA_V0
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalID;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct USN_JOURNAL_DATA_V0
    {
        public ulong UsnJournalID;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    // Union FILE_ID_DESCRIPTOR : le membre le plus large (GUID) fait 16 octets → on dimensionne
    // explicitement la zone à 16 octets pour que dwSize/Marshal.SizeOf soient corrects.
    [StructLayout(LayoutKind.Explicit)]
    private struct FILE_ID_DESCRIPTOR
    {
        [FieldOffset(0)] public uint dwSize;
        [FieldOffset(4)] public int Type;
        [FieldOffset(8)] public long FileId;
        [FieldOffset(16)] public long FileIdHigh;
    }
}
