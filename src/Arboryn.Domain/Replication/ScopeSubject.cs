using Arboryn.Domain.Enums;

namespace Arboryn.Domain.Replication;

/// <summary>
/// Faits d'un <c>LogicalFile</c> qu'une <see cref="ScopeExpression"/> évalue pour décider
/// s'il est dans le périmètre de réplication d'un volume (Inc 10). Volontairement minimal :
/// catégorie, sous-catégorie (chemin hiérarchique, ex. « Investissements/Appartement »),
/// et année (issue des métadonnées, ex. année de prise de vue / de publication).
/// </summary>
public sealed record ScopeSubject(MediaCategory Category, string? Subcategory = null, int? Year = null);
