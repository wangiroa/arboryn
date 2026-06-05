using System.IO;

namespace Arboryn.Domain.ValueObjects;

/// <summary>
/// Chemin absolu Windows, normalisé (séparateurs <c>\</c>, sans séparateur final).
/// Type purement immuable, sans I/O : la validation est syntaxique, jamais
/// vérifiée contre le système de fichiers.
/// </summary>
public readonly record struct FilePath
{
    public string Value { get; }

    private FilePath(string value) => Value = value;

    /// <summary>
    /// Construit un <see cref="FilePath"/> à partir d'un chemin absolu.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Si le chemin est vide ou n'est pas enraciné (chemin relatif).
    /// </exception>
    public static FilePath From(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Le chemin ne peut pas être vide.", nameof(path));
        }

        var normalized = Normalize(path);

        if (!Path.IsPathRooted(normalized))
        {
            throw new ArgumentException($"Le chemin doit être absolu : '{path}'.", nameof(path));
        }

        return new FilePath(normalized);
    }

    /// <summary>Nom de fichier (dernier segment), extension comprise.</summary>
    public string FileName => Path.GetFileName(Value);

    /// <summary>Extension avec le point (par ex. <c>.pdf</c>), ou chaîne vide.</summary>
    public string Extension => Path.GetExtension(Value);

    /// <summary>Combine ce chemin avec un chemin relatif pour obtenir un chemin absolu.</summary>
    public FilePath Combine(RelativePath relative) =>
        From(Path.Combine(Value, relative.Value));

    /// <summary>
    /// Forme « extended-length » (<c>\\?\</c>) utilisée par les API natives Win32
    /// pour dépasser la limite MAX_PATH. Les chemins UNC reçoivent le préfixe <c>\\?\UNC\</c>.
    /// </summary>
    public string ToExtendedLengthPath()
    {
        if (Value.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return Value;
        }

        return Value.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + Value[2..]
            : @"\\?\" + Value;
    }

    public override string ToString() => Value;

    private static string Normalize(string path)
    {
        var s = path.Replace('/', '\\');

        // Conserve un éventuel préfixe UNC ou extended-length à deux barres ;
        // ne taille que le séparateur final, sauf pour une racine de lecteur (« C:\ »).
        if (s.Length > 3 && s.EndsWith('\\'))
        {
            s = s.TrimEnd('\\');
        }

        return s;
    }
}
