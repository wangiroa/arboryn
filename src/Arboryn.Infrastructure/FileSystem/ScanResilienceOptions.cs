namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Politique de ré-essai pour les accès fichier sur volumes réseau (UNC/SMB, Inc 9).
/// Les partages réseau subissent des erreurs transitoires (timeouts, « nom réseau
/// momentanément indisponible ») : un ré-essai à back-off exponentiel évite de perdre
/// silencieusement des fichiers lors d'un scan. Les volumes locaux ne ré-essaient pas.
/// </summary>
public sealed record ScanResilienceOptions(int MaxAttempts, TimeSpan BaseDelay, TimeSpan MaxDelay)
{
    /// <summary>3 tentatives, back-off 200 ms → 400 ms (plafonné à 2 s).</summary>
    public static ScanResilienceOptions Default { get; } =
        new(MaxAttempts: 3, BaseDelay: TimeSpan.FromMilliseconds(200), MaxDelay: TimeSpan.FromSeconds(2));
}
