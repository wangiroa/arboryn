using System.Globalization;
using System.Text.RegularExpressions;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Metadata;
using Arboryn.Domain.ValueObjects;
using VersOne.Epub;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Adapter <see cref="IContentMetadataReader"/> basé sur VersOne.Epub. Lit l'OPF d'un
/// EPUB (titre, auteur(s), éditeur, langue, ISBN, année). L'OPF est structuré et
/// fiable : confiance élevée.
/// </summary>
public sealed partial class EpubMetadataReader : IContentMetadataReader
{
    public string Source => MetadataSources.EpubOpf;

    public double Confidence => 0.9;

    public bool CanRead(MediaCategory category) => category == MediaCategory.Book;

    public async Task<IReadOnlyDictionary<string, string>> ReadAsync(FilePath path, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        var book = await EpubReader.ReadBookAsync(path.Value).ConfigureAwait(false);

        AddIfPresent(result, MetadataKeys.Title, book.Title);

        if (book.AuthorList is { Count: > 0 } authors)
        {
            var joined = string.Join("; ", authors.Where(a => !string.IsNullOrWhiteSpace(a)));
            AddIfPresent(result, MetadataKeys.Author, joined);
        }

        var metadata = book.Schema?.Package?.Metadata;
        if (metadata is not null)
        {
            if (metadata.Languages is { Count: > 0 } languages)
            {
                AddIfPresent(result, MetadataKeys.Language, languages[0].Language);
            }

            if (metadata.Publishers is { Count: > 0 } publishers)
            {
                AddIfPresent(result, MetadataKeys.Publisher, publishers[0].Publisher);
            }

            foreach (var identifier in metadata.Identifiers)
            {
                if (Isbn.TryExtract(identifier.Identifier, out var isbn))
                {
                    result[MetadataKeys.Isbn] = isbn;
                    break;
                }
            }

            var year = metadata.Dates
                .Select(d => ExtractYear(d.Date))
                .FirstOrDefault(y => y is not null);
            if (year is { } y)
            {
                result[MetadataKeys.Year] = y.ToString(CultureInfo.InvariantCulture);
            }
        }

        return result;
    }

    private static int? ExtractYear(string? date)
    {
        if (string.IsNullOrWhiteSpace(date))
        {
            return null;
        }

        var match = YearRegex().Match(date);
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
