namespace Arboryn.Domain.Enums;

/// <summary>
/// Cycle de vie d'une <see cref="Entities.Operation"/> dans le journal. Les valeurs
/// correspondent à la contrainte CHECK de la table <c>operations</c>.
/// </summary>
public enum OperationStatus
{
    /// <summary>Planifiée, pas encore exécutée (par ex. volume hors-ligne).</summary>
    Pending,

    /// <summary>En cours d'exécution.</summary>
    InProgress,

    /// <summary>Exécutée avec succès.</summary>
    Completed,

    /// <summary>Échec à l'exécution.</summary>
    Failed,

    /// <summary>Abandonnée avant exécution.</summary>
    Cancelled,

    /// <summary>Annulée après exécution (rejouée à l'envers).</summary>
    Undone
}
