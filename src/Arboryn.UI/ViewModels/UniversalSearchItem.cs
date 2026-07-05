using System.Collections.Generic;
using Arboryn.Application.Inventory;

namespace Arboryn.UI.ViewModels;

/// <summary>Suggestion de la recherche universelle (barre du haut) : une œuvre et où elle se trouve.</summary>
public sealed class UniversalSearchItem
{
    public UniversalSearchItem(CrossVolumeSearchResult result)
    {
        FileName = result.FileName;
        Detail = $"{CategoryLabels.Of(result.Category)} · présent sur {string.Join(", ", result.VolumeNames)}";
    }

    public string FileName { get; }

    public string Detail { get; }
}
