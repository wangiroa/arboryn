using System.IO;
using Microsoft.Extensions.Logging;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Exécute une opération d'I/O fichier avec ré-essai à back-off exponentiel sur erreur
/// transitoire (Inc 9, robustesse UNC/NAS). Le ré-essai n'est tenté que si
/// <paramref name="resilient"/> est vrai (chemins réseau) ; les erreurs définitives
/// (fichier/dossier absent, accès refusé) ne sont jamais ré-essayées.
/// </summary>
public static class ScanResilience
{
    /// <summary>
    /// Exécute <paramref name="action"/>, en ré-essayant les erreurs transitoires jusqu'à
    /// <see cref="ScanResilienceOptions.MaxAttempts"/>. <paramref name="sleep"/> est injectable
    /// pour les tests (back-off sans délai réel). La dernière exception est propagée à l'appelant.
    /// </summary>
    public static T Execute<T>(
        Func<T> action,
        bool resilient,
        ScanResilienceOptions options,
        ILogger logger,
        string context,
        Action<TimeSpan>? sleep = null)
    {
        sleep ??= d => Thread.Sleep(d);
        var maxAttempts = resilient ? Math.Max(1, options.MaxAttempts) : 1;
        var delay = options.BaseDelay;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxAttempts)
            {
                logger.LogDebug(
                    ex, "Accès réseau transitoire (tentative {Attempt}/{Max}, attente {Delay} ms) : {Context}",
                    attempt, maxAttempts, delay.TotalMilliseconds, context);
                sleep(delay);
                var next = delay.TotalMilliseconds * 2;
                delay = TimeSpan.FromMilliseconds(Math.Min(next, options.MaxDelay.TotalMilliseconds));
            }
        }
    }

    /// <summary>
    /// Erreur réseau transitoire (à ré-essayer) : <see cref="IOException"/> générique,
    /// hors absence définitive (fichier/dossier introuvable). <see cref="UnauthorizedAccessException"/>
    /// est définitive (permissions) et n'est pas considérée transitoire.
    /// </summary>
    public static bool IsTransient(Exception ex) =>
        ex is IOException and not (FileNotFoundException or DirectoryNotFoundException);
}
