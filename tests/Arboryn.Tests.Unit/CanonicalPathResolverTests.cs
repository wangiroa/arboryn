using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Taxonomy;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class CanonicalPathResolverTests
{
    private static readonly CategoryTaxonomy Taxonomy = new(
        MediaCategory.Audiobook,
        PathTemplate: "Livres audio/{{ author }}",
        NameTemplate: "{{ author }} - {{ title }}.{{ ext }}",
        RequiredFields: new[] { "author", "title" });

    [Fact]
    public void Resolve_ReturnsNull_WhenRequiredFieldMissing()
    {
        var resolver = new CanonicalPathResolver(new EchoRenderer());
        var fields = new Dictionary<string, string?> { ["author"] = "Asimov" }; // title manquant

        resolver.Resolve(Taxonomy, fields).Should().BeNull();
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenRequiredFieldBlank()
    {
        var resolver = new CanonicalPathResolver(new EchoRenderer());
        var fields = new Dictionary<string, string?> { ["author"] = "Asimov", ["title"] = "  " };

        resolver.Resolve(Taxonomy, fields).Should().BeNull();
    }

    [Fact]
    public void Resolve_SanitizesRenderedOutput()
    {
        // Renderer renvoie un chemin avec '/' et un nom avec un caractère interdit.
        var resolver = new CanonicalPathResolver(new FixedRenderer(path: "a/b", name: "na:me.txt"));
        var fields = new Dictionary<string, string?> { ["author"] = "x", ["title"] = "y" };

        var placement = resolver.Resolve(Taxonomy, fields);

        placement.Should().NotBeNull();
        placement!.RelativeDirectory.Should().Be(@"a\b");
        placement.FileName.Should().Be("na me.txt");
        placement.RelativePath.Should().Be(@"a\b\na me.txt");
    }

    /// <summary>Renderer minimal : remplace <c>{{ key }}</c> par la valeur du champ.</summary>
    private sealed class EchoRenderer : ITemplateRenderer
    {
        public string Render(string template, IReadOnlyDictionary<string, string?> fields)
        {
            var result = template;
            foreach (var (key, value) in fields)
            {
                result = result.Replace("{{ " + key + " }}", value ?? string.Empty);
            }

            return result;
        }
    }

    private sealed class FixedRenderer : ITemplateRenderer
    {
        private readonly string _path;
        private readonly string _name;

        public FixedRenderer(string path, string name)
        {
            _path = path;
            _name = name;
        }

        public string Render(string template, IReadOnlyDictionary<string, string?> fields)
            => template.Contains("ext") ? _name : _path;
    }
}
