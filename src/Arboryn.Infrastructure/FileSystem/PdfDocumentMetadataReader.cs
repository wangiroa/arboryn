using System.Globalization;
using System.Text.RegularExpressions;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Metadata;
using Arboryn.Domain.ValueObjects;
using UglyToad.PdfPig;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Adapter <see cref="IContentMetadataReader"/> basé sur PdfPig. Lit le dictionnaire
/// <c>Info</c> d'un PDF (titre, auteur, sujet, date de création). Ces champs sont
/// souvent absents ou bruités dans les PDF réels : confiance modérée.
/// </summary>
public sealed partial class PdfDocumentMetadataReader : IContentMetadataReader
{
    public string Source => MetadataSources.PdfInfo;

    public double Confidence => 0.6;

    public bool CanRead(MediaCategory category) => category == MediaCategory.OtherDocument;

    public Task<IReadOnlyDictionary<string, string>> ReadAsync(FilePath path, CancellationToken cancellationToken)
        => Task.Run<IReadOnlyDictionary<string, string>>(() => Read(path), cancellationToken);

    private static Dictionary<string, string> Read(FilePath path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        // Parsing tolérant : les PDF réels ont souvent une table xref incohérente.
        using var document = PdfDocument.Open(path.Value, new ParsingOptions { UseLenientParsing = true });
        var info = document.Information;

        AddIfPresent(result, MetadataKeys.Title, info.Title);
        AddIfPresent(result, MetadataKeys.Author, info.Author);
        AddIfPresent(result, MetadataKeys.Subtitle, info.Subject);
        AddIfPresent(result, MetadataKeys.Publisher, info.Producer);

        var year = ExtractYear(info.CreationDate);
        if (year is { } y)
        {
            result[MetadataKeys.Year] = y.ToString(CultureInfo.InvariantCulture);
        }

        // Un PDF qui annonce un ISBN (souvent dans le sujet ou les mots-clés) est en
        // pratique un ebook : on expose l'ISBN, exploité ensuite par le raffinement de catégorie.
        foreach (var field in new[] { info.Subject, info.Keywords, info.Title })
        {
            if (Isbn.TryExtract(field, out var isbn))
            {
                result[MetadataKeys.Isbn] = isbn;
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Extrait l'année d'une date PDF (« D:YYYYMMDDHHmmSS±hh'mm' ») ou de toute chaîne
    /// contenant une année plausible (1500–2099).
    /// </summary>
    private static int? ExtractYear(string? pdfDate)
    {
        if (string.IsNullOrWhiteSpace(pdfDate))
        {
            return null;
        }

        var match = YearRegex().Match(pdfDate);
        return match.Success && int.TryParse(match.Value, out var year) ? year : null;
    }

    private static void AddIfPresent(IDictionary<string, string> dict, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            dict[key] = value.Trim();
        }
    }

    [GeneratedRegex(@"(?:15|16|17|18|19|20)\d{2}")]
    private static partial Regex YearRegex();
}
