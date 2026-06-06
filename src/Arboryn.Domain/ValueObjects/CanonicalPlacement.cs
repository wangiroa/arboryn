namespace Arboryn.Domain.ValueObjects;

/// <summary>
/// Emplacement canonique cible d'un fichier sur son volume : répertoire relatif (séparateurs
/// Windows) et nom de fichier, tous deux déjà assainis. <see cref="RelativePath"/> combine les deux.
/// </summary>
public sealed record CanonicalPlacement(string RelativeDirectory, string FileName)
{
    public string RelativePath =>
        RelativeDirectory.Length == 0 ? FileName : $"{RelativeDirectory}\\{FileName}";

    public override string ToString() => RelativePath;
}
