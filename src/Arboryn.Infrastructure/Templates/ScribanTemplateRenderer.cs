using System.Collections.Concurrent;
using System.Globalization;
using Arboryn.Application.Abstractions;
using Scriban;
using Scriban.Runtime;

namespace Arboryn.Infrastructure.Templates;

/// <summary>
/// Implémentation <see cref="ITemplateRenderer"/> basée sur Scriban, en mode restreint :
/// seuls les champs fournis et la fonction <c>format</c> sont accessibles au template
/// (aucune I/O, aucun objet .NET exposé). Les variables absentes rendent une chaîne vide.
/// Les templates parsés sont mis en cache (réutilisés pour des milliers de fichiers).
/// </summary>
public sealed class ScribanTemplateRenderer : ITemplateRenderer
{
    private readonly ConcurrentDictionary<string, Template> _cache = new(StringComparer.Ordinal);

    public string Render(string template, IReadOnlyDictionary<string, string?> fields)
    {
        var parsed = _cache.GetOrAdd(template, static t => Template.Parse(t));
        if (parsed.HasErrors)
        {
            throw new InvalidOperationException(
                "Template invalide : " + string.Join(" ; ", parsed.Messages.Select(m => m.Message)));
        }

        var scope = new ScriptObject();
        foreach (var (key, value) in fields)
        {
            scope[key] = value;
        }

        scope.Import("format", new Func<string?, string, string>(FormatValue));

        var context = new TemplateContext
        {
            EnableRelaxedMemberAccess = true, // membre absent → null (rendu vide), pas d'exception
            StrictVariables = false,
            LoopLimit = 1000,
        };
        context.PushGlobal(scope);

        return parsed.Render(context);
    }

    /// <summary>Formate une valeur numérique (« 1 » → « 01 ») ; renvoie la valeur telle quelle sinon.</summary>
    private static string FormatValue(string? value, string format)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number.ToString(format, CultureInfo.InvariantCulture)
            : value;
    }
}
