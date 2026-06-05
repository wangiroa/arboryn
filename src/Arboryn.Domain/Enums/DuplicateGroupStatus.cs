namespace Arboryn.Domain.Enums;

/// <summary>
/// État de revue d'un <see cref="Entities.DuplicateGroup"/>. Les valeurs
/// correspondent à la contrainte CHECK de la table <c>duplicate_groups</c>.
/// </summary>
public enum DuplicateGroupStatus
{
    /// <summary>Détecté, pas encore examiné par l'utilisateur.</summary>
    Pending,

    /// <summary>Examiné, décision pas encore appliquée.</summary>
    Reviewed,

    /// <summary>Résolu (doublons supprimés / conservés).</summary>
    Resolved,

    /// <summary>Écarté : l'utilisateur considère que ce ne sont pas des doublons.</summary>
    Dismissed
}
