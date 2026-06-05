namespace Arboryn.Domain.Enums;

/// <summary>
/// Nature d'une opération sur fichier. Seul <see cref="Delete"/> est utilisé en
/// Inc 1 ; les autres servent à l'uniformisation (Inc 6) et à la réplication
/// (Inc 10), mais l'énumération est complète dès maintenant.
/// </summary>
public enum OperationKind
{
    /// <summary>Renommer un fichier au sein du même répertoire.</summary>
    Rename,

    /// <summary>Déplacer un fichier vers un autre répertoire du même volume.</summary>
    Move,

    /// <summary>Copier un fichier vers un autre volume.</summary>
    Copy,

    /// <summary>Supprimer un fichier (corbeille par défaut) — Inc 1.</summary>
    Delete,

    /// <summary>Réécrire les métadonnées dans le fichier lui-même.</summary>
    MetadataWriteback
}
