using System;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.UI.Services;

/// <summary>
/// Volume sur lequel portent les opérations courantes (scan, détection, uniformisation,
/// triage, enrichissement, inventaire). Partagé entre tous les ViewModels (Inc 9).
///
/// Initialisé sur le volume « default » : tant qu'aucun volume réel n'est enrôlé, le
/// comportement est strictement identique à l'avant-multi-volume (aucune régression).
/// Un scan enrôle/reconnaît le volume du dossier choisi et le rend actif ; la page Volumes
/// permet aussi de sélectionner explicitement le volume actif.
/// </summary>
public sealed class ActiveVolumeContext
{
    public VolumeId Current { get; private set; } = VolumeId.Default;

    public string CurrentName { get; private set; } = "Volume par défaut";

    /// <summary>Émis quand le volume actif change, pour permettre aux vues de se recharger.</summary>
    public event EventHandler? Changed;

    public void Set(VolumeId id, string name)
    {
        if (Current == id && string.Equals(CurrentName, name, StringComparison.Ordinal))
        {
            return;
        }

        Current = id;
        CurrentName = string.IsNullOrWhiteSpace(name) ? id.Value : name;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
