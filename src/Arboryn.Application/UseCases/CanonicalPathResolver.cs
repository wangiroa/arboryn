using Arboryn.Application.Abstractions;
using Arboryn.Domain.Taxonomy;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Calcule l'emplacement canonique cible d'un fichier : rend les templates de chemin et de
/// nom de sa taxonomie à partir de ses champs, puis assainit pour Windows. Renvoie <c>null</c>
/// si un champ requis manque (le fichier ne peut pas encore être uniformisé).
/// </summary>
public sealed class CanonicalPathResolver
{
    private readonly ITemplateRenderer _renderer;

    public CanonicalPathResolver(ITemplateRenderer renderer)
        => _renderer = renderer;

    public CanonicalPlacement? Resolve(
        CategoryTaxonomy taxonomy, IReadOnlyDictionary<string, string?> fields)
    {
        foreach (var required in taxonomy.RequiredFields)
        {
            if (!fields.TryGetValue(required, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
        }

        var renderedDirectory = _renderer.Render(taxonomy.PathTemplate, fields);
        var renderedName = _renderer.Render(taxonomy.NameTemplate, fields);

        var directory = WindowsPathSanitizer.SanitizeRelativeDirectory(renderedDirectory);
        var fileName = WindowsPathSanitizer.SanitizeFileName(renderedName);

        return new CanonicalPlacement(directory, fileName);
    }
}
