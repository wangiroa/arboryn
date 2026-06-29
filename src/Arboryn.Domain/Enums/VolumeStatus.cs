namespace Arboryn.Domain.Enums;

/// <summary>
/// État de connexion d'un volume au moment courant. Un volume <see cref="Offline"/>
/// conserve son index pour comparaison (« memorized volume ») ; ses opérations en
/// attente sont reprises au rebranchement (Inc 10).
/// </summary>
public enum VolumeStatus
{
    /// <summary>Connecté et accessible.</summary>
    Online,

    /// <summary>Connu mais non connecté actuellement.</summary>
    Offline,

    /// <summary>Jamais identifié de façon stable (état initial).</summary>
    Unknown
}
