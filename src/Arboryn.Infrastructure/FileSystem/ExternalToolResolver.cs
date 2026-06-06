namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Localise un exécutable externe embarqué (fpcalc, ffprobe…). Ordre de résolution :
/// <list type="number">
///   <item>chemin explicitement configuré (settings / variable d'environnement) ;</item>
///   <item>dossier <c>tools/</c> à côté de l'application (modèle « binaire embarqué ») ;</item>
///   <item>recherche dans le <c>PATH</c> du système.</item>
/// </list>
/// Renvoie <c>null</c> si l'outil reste introuvable — l'appelant décide alors d'ignorer
/// l'opération plutôt que d'échouer.
/// </summary>
public sealed class ExternalToolResolver
{
    private readonly string _baseDirectory;
    private readonly Func<string?> _pathProvider;

    public ExternalToolResolver(string baseDirectory, Func<string?>? pathProvider = null)
    {
        _baseDirectory = baseDirectory;
        _pathProvider = pathProvider ?? (() => Environment.GetEnvironmentVariable("PATH"));
    }

    public string? Resolve(string executableName, string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var bundled = Path.Combine(_baseDirectory, "tools", executableName);
        if (File.Exists(bundled))
        {
            return bundled;
        }

        var pathValue = _pathProvider();
        if (!string.IsNullOrEmpty(pathValue))
        {
            foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var candidate = Path.Combine(directory, executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
