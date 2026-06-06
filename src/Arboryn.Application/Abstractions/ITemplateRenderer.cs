namespace Arboryn.Application.Abstractions;

/// <summary>
/// Rend un template (chemin ou nom canonique) à partir d'un ensemble de champs. Les variables
/// absentes rendent une chaîne vide ; aucune I/O n'est exposée au template (rendu restreint).
/// </summary>
public interface ITemplateRenderer
{
    string Render(string template, IReadOnlyDictionary<string, string?> fields);
}
