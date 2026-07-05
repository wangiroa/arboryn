using Arboryn.Application.Abstractions;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Fournit le nom d'hôte de la machine locale via <see cref="Environment.MachineName"/>
/// (Inc 13). Aucune API Windows-only → pas d'attribut de plateforme requis.
/// </summary>
public sealed class EnvironmentMachineProvider : ILocalMachineProvider
{
    public string Hostname => Environment.MachineName;
}
