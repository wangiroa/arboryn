namespace Arboryn.Domain.Enums;

/// <summary>
/// Catégorie de média utilisée pour filtrer la vue des doublons. Contrairement à
/// <see cref="MediaCategory"/> (qui sert au placement canonique et n'attribue qu'une
/// catégorie par fichier), une extension peut appartenir à PLUSIEURS catégories de filtre :
/// un PDF est à la fois un document bureautique, un ebook et une BD potentielle.
/// </summary>
public enum MediaFilterType
{
    Audio,
    Video,
    Photo,
    Comic,
    Document,
    Ebook,
}
