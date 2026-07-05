namespace Arboryn.Infrastructure.Database;

/// <summary>
/// Garde mono-écrivain (Inc 13, A2) : verrou exclusif sur <c>{dbPath}.lock</c> tenu pour toute
/// la durée du processus. Empêche deux instances (ou deux PC pointant la même base partagée via
/// SMB) d'écrire simultanément dans le fichier — la cause n° 1 de corruption SQLite hors disque
/// local. Le verrou est un handle OS : il se libère automatiquement à la fin du processus (même
/// en cas de crash), et <see cref="FileOptions.DeleteOnClose"/> retire le fichier au passage.
/// </summary>
public static class DatabaseWriteLock
{
    /// <summary>
    /// Tente d'acquérir le verrou. Renvoie le handle à conserver (à libérer à la fermeture),
    /// ou <c>null</c> si la base est déjà ouverte ailleurs (verrou détenu par un autre processus).
    /// </summary>
    public static FileStream? TryAcquire(string databasePath)
    {
        var lockPath = databasePath + ".lock";
        try
        {
            var dir = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return new FileStream(
                lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
                FileOptions.DeleteOnClose);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
