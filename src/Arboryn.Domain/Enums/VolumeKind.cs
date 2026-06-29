namespace Arboryn.Domain.Enums;

/// <summary>
/// Nature physique d'un volume. Détectée à l'enrôlement (Inc 9) via le type de
/// lecteur Windows, mais éditable par l'utilisateur. <see cref="Default"/> est
/// réservé à la ligne « default » créée dès l'Inc 3, qui héberge toutes les
/// instances tant qu'un volume réel n'a pas été identifié.
/// </summary>
public enum VolumeKind
{
    /// <summary>Disque interne fixe.</summary>
    Internal,

    /// <summary>Disque externe / amovible (USB).</summary>
    External,

    /// <summary>Partage réseau (NAS / SMB).</summary>
    Nas,

    /// <summary>Autre support non classé.</summary>
    Other,

    /// <summary>Volume logique « default » (pré-multi-volume).</summary>
    Default
}
