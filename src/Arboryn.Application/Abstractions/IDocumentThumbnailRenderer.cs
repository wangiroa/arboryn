using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Produit une miniature de la première page d'un document (rasterisation PDF, ou
/// redimensionnement d'une image) pour la grille de triage. Se dégrade proprement : renvoie
/// <c>null</c> si le moteur natif est absent ou si le rendu échoue.
/// </summary>
public interface IDocumentThumbnailRenderer
{
    bool CanRender(string extension);

    /// <summary>
    /// Rend la miniature de <paramref name="source"/> dans <paramref name="outputDirectory"/>
    /// et renvoie le chemin du PNG produit, ou <c>null</c> en cas d'échec/indisponibilité.
    /// </summary>
    Task<FilePath?> RenderFirstPageAsync(
        FilePath source, string outputDirectory, int maxWidth, CancellationToken cancellationToken);
}
