using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Implémentation de la corbeille via l'API moderne <c>IFileOperation</c> (COM).
/// La suppression utilise FOF_ALLOWUNDO (envoi corbeille) et un progress sink pour
/// capturer le chemin du fichier dans la corbeille ; la restauration est un simple
/// déplacement inverse — robuste et indépendant des verbes Shell localisés.
///
/// Les opérations COM s'exécutent sur un thread STA dédié.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RecycleBin : IRecycleBin
{
    // FOF / FOFX flags
    private const uint FOF_SILENT = 0x0004;
    private const uint FOF_NOCONFIRMATION = 0x0010;
    private const uint FOF_ALLOWUNDO = 0x0040;
    private const uint FOF_NOERRORUI = 0x0400;
    private const uint FOFX_RECYCLEONDELETE = 0x00080000;

    private const uint SIGDN_FILESYSPATH = 0x80058000;

    private readonly ILogger<RecycleBin> _logger;

    public RecycleBin(ILogger<RecycleBin> logger) => _logger = logger;

    public Task<FilePath?> SendToRecycleBinAsync(FilePath path, CancellationToken cancellationToken)
        => RunStaAsync<FilePath?>(() =>
        {
            var operation = (IFileOperation)new FileOperation();
            operation.SetOperationFlags(
                FOF_ALLOWUNDO | FOFX_RECYCLEONDELETE | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI);

            var sink = new RecycleSink();
            operation.Advise(sink, out var cookie);
            try
            {
                operation.DeleteItem(CreateShellItem(path.Value), null);
                operation.PerformOperations();
            }
            finally
            {
                operation.Unadvise(cookie);
            }

            return sink.RecycledPath is { } recycled ? FilePath.From(recycled) : null;
        });

    public Task<bool> RestoreAsync(FilePath recycledPath, FilePath originalPath, CancellationToken cancellationToken)
        => RunStaAsync(() =>
        {
            try
            {
                var destinationFolder = System.IO.Path.GetDirectoryName(originalPath.Value);
                if (destinationFolder is null)
                {
                    return false;
                }

                var operation = (IFileOperation)new FileOperation();
                operation.SetOperationFlags(FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI);
                operation.MoveItem(
                    CreateShellItem(recycledPath.Value),
                    CreateShellItem(destinationFolder),
                    System.IO.Path.GetFileName(originalPath.Value),
                    null);
                operation.PerformOperations();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Échec de la restauration vers {Path}", originalPath);
                return false;
            }
        });

    private static IShellItem CreateShellItem(string path)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out var item);
        return item;
    }

    private static Task<T> RunStaAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    /// <summary>Capture le chemin du fichier nouvellement créé dans la corbeille.</summary>
    private sealed class RecycleSink : IFileOperationProgressSink
    {
        public string? RecycledPath { get; private set; }

        public void PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem? psiNewlyCreated)
        {
            if (psiNewlyCreated is null)
            {
                return;
            }

            psiNewlyCreated.GetDisplayName(SIGDN_FILESYSPATH, out var ptr);
            if (ptr != IntPtr.Zero)
            {
                RecycledPath = Marshal.PtrToStringUni(ptr);
                Marshal.FreeCoTaskMem(ptr);
            }
        }

        public void StartOperations() { }
        public void FinishOperations(int hrResult) { }
        public void PreRenameItem(uint dwFlags, IShellItem psiItem, string pszNewName) { }
        public void PostRenameItem(uint dwFlags, IShellItem psiItem, string pszNewName, int hrRename, IShellItem psiNewlyCreated) { }
        public void PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName) { }
        public void PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName, int hrMove, IShellItem psiNewlyCreated) { }
        public void PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName) { }
        public void PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName, int hrCopy, IShellItem psiNewlyCreated) { }
        public void PreDeleteItem(uint dwFlags, IShellItem psiItem) { }
        public void PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, string pszNewName) { }
        public void PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, string pszNewName, string pszTemplateName, uint dwFileAttributes, int hrNew, IShellItem psiNewItem) { }
        public void UpdateProgress(uint iWorkTotal, uint iWorkSoFar) { }
        public void ResetTimer() { }
        public void PauseTimer() { }
        public void ResumeTimer() { }
    }
}

[ComImport]
[Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
[SupportedOSPlatform("windows")]
internal class FileOperation
{
}

[ComImport]
[Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[SupportedOSPlatform("windows")]
internal interface IFileOperation
{
    void Advise(IFileOperationProgressSink pfops, out uint pdwCookie);
    void Unadvise(uint dwCookie);
    void SetOperationFlags(uint dwOperationFlags);
    void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
    void SetProgressDialog(IntPtr popd);
    void SetProperties(IntPtr pproparray);
    void SetOwnerWindow(IntPtr hwndOwner);
    void ApplyPropertiesToItem(IShellItem psiItem);
    void ApplyPropertiesToItems(IntPtr punkItems);
    void RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IFileOperationProgressSink? pfopsItem);
    void RenameItems(IntPtr pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
    void MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IFileOperationProgressSink? pfopsItem);
    void MoveItems(IntPtr punkItems, IShellItem psiDestinationFolder);
    void CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszCopyName, IFileOperationProgressSink? pfopsItem);
    void CopyItems(IntPtr punkItems, IShellItem psiDestinationFolder);
    void DeleteItem(IShellItem psiItem, IFileOperationProgressSink? pfopsItem);
    void DeleteItems(IntPtr punkItems);
    void NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName, IFileOperationProgressSink? pfopsItem);
    void PerformOperations();
    [return: MarshalAs(UnmanagedType.Bool)] bool GetAnyOperationsAborted();
}

[ComImport]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[SupportedOSPlatform("windows")]
internal interface IShellItem
{
    void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
    void GetParent(out IShellItem ppsi);
    void GetDisplayName(uint sigdnName, out IntPtr ppszName);
    void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    void Compare(IShellItem psi, uint hint, out int piOrder);
}

[ComImport]
[Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[SupportedOSPlatform("windows")]
internal interface IFileOperationProgressSink
{
    void StartOperations();
    void FinishOperations(int hrResult);
    void PreRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
    void PostRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrRename, IShellItem psiNewlyCreated);
    void PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
    void PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrMove, IShellItem psiNewlyCreated);
    void PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
    void PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrCopy, IShellItem psiNewlyCreated);
    void PreDeleteItem(uint dwFlags, IShellItem psiItem);
    void PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem? psiNewlyCreated);
    void PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
    void PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName, uint dwFileAttributes, int hrNew, IShellItem psiNewItem);
    void UpdateProgress(uint iWorkTotal, uint iWorkSoFar);
    void ResetTimer();
    void PauseTimer();
    void ResumeTimer();
}
