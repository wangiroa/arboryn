using Arboryn.Domain.ValueObjects;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// Choix de volume source dans la vue des doublons (Inc 9). L'égalité par valeur (record)
/// permet au ComboBox de resélectionner l'option correspondant au volume actif après
/// rechargement de la liste.
/// </summary>
public sealed record VolumeChoice(VolumeId Id, string Name)
{
    public string Label => Name;
}
