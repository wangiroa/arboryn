using Arboryn.Domain.Enums;

namespace Arboryn.UI.ViewModels;

/// <summary>
/// Option de filtre par type de média proposée dans la vue des doublons. <see cref="Type"/> à
/// <c>null</c> représente « tous les types » (aucun filtrage).
/// </summary>
public sealed record MediaFilterOption(string Label, MediaFilterType? Type);
