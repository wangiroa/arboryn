namespace Arboryn.Application.Abstractions;

/// <summary>
/// Fournit l'identité de la machine locale (Inc 13). Abstrait <c>Environment.MachineName</c>
/// pour garder la couche Application pure (sans dépendance à l'environnement d'exécution)
/// et testable via un double.
/// </summary>
public interface ILocalMachineProvider
{
    /// <summary>Nom d'hôte du PC courant (stable, sert de clé d'identité machine).</summary>
    string Hostname { get; }
}
