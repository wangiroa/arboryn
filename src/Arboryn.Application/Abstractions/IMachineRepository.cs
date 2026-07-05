using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Dépôt des machines (PC) propriétaires de volumes (Inc 13). Chaque volume interne ou
/// externe est rattaché à une machine ; les volumes NAS restent agnostiques. L'identité
/// est le nom d'hôte, capté à l'enrôlement, ce qui permet de nommer sans ambiguïté le PC
/// hôte dans un catalogue partagé entre plusieurs postes.
/// </summary>
public interface IMachineRepository
{
    /// <summary>
    /// Renvoie l'id de la machine locale (nom d'hôte <paramref name="hostname"/>), en la
    /// créant si absente (nom initial = hostname), et met à jour son <c>last_seen_at</c>.
    /// Idempotent : deux appels pour le même hôte renvoient le même id.
    /// </summary>
    Task<MachineId> EnsureLocalAsync(string hostname, CancellationToken cancellationToken);

    /// <summary>Machine par son id, ou <c>null</c>.</summary>
    Task<MachineRecord?> GetAsync(MachineId id, CancellationToken cancellationToken);

    /// <summary>Toutes les machines connues, triées par nom.</summary>
    Task<IReadOnlyList<MachineRecord>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Renomme une machine (change le libellé, jamais le nom d'hôte d'identité).</summary>
    Task RenameAsync(MachineId id, string newName, CancellationToken cancellationToken);
}

/// <summary>
/// État persistant d'une machine. <see cref="Hostname"/> est la clé d'identité stable ;
/// <see cref="Name"/> est un libellé éditable (par défaut égal au hostname).
/// </summary>
public sealed record MachineRecord(
    MachineId Id,
    string Name,
    string Hostname,
    DateTime FirstSeenAt,
    DateTime? LastSeenAt);
