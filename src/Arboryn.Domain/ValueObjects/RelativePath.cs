namespace Arboryn.Domain.ValueObjects;

/// <summary>
/// Chemin relatif à la racine d'un volume, séparateurs normalisés en <c>\</c>,
/// sans séparateur initial ni final. Utilisé pour comparer des emplacements
/// indépendamment du point de montage du volume.
/// </summary>
public readonly record struct RelativePath
{
    public string Value { get; }

    private RelativePath(string value) => Value = value;

    /// <summary>
    /// Construit un <see cref="RelativePath"/> à partir d'un chemin relatif.
    /// </summary>
    /// <exception cref="ArgumentException">Si le chemin est vide.</exception>
    public static RelativePath From(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Le chemin relatif ne peut pas être vide.", nameof(path));
        }

        var normalized = path.Replace('/', '\\').Trim('\\');
        return new RelativePath(normalized);
    }

    public override string ToString() => Value;
}
