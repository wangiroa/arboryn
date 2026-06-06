using Arboryn.Domain.Enums;

namespace Arboryn.Domain.Taxonomy;

/// <summary>
/// Définition canonique d'une catégorie : templates de chemin et de nom (syntaxe Scriban),
/// champs requis pour valider, et version. C'est la « vérité » vers laquelle Arboryn fait
/// converger l'arborescence physique.
/// </summary>
public sealed record CategoryTaxonomy(
    MediaCategory Category,
    string PathTemplate,
    string NameTemplate,
    IReadOnlyList<string> RequiredFields,
    int Version = 1);
