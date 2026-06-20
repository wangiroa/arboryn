using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Taxonomy;
using Arboryn.Infrastructure.Persistence;
using Arboryn.Infrastructure.Templates;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

/// <summary>Tests Inc 6 : moteur Scriban, calcul du chemin canonique, dépôt de taxonomie.</summary>
public class TaxonomyTests
{
    private static readonly ITemplateRenderer Renderer = new ScribanTemplateRenderer();

    [Fact]
    public void Scriban_RendersConditional_AndFormatPad()
    {
        var taxonomy = DefaultTaxonomies.For(MediaCategory.Audiobook)!;
        var fields = new Dictionary<string, string?>
        {
            ["author"] = "Asimov",
            ["series"] = "Fondation",
            ["volume"] = "1",
            ["title"] = "Fondation",
            ["ext"] = "m4b",
        };

        Renderer.Render(taxonomy.NameTemplate, fields)
            .Should().Be("Asimov - Fondation - 01 - Fondation.m4b");
    }

    [Fact]
    public void Scriban_OmitsConditionalBlock_WhenVariableMissing()
    {
        var taxonomy = DefaultTaxonomies.For(MediaCategory.Audiobook)!;
        var fields = new Dictionary<string, string?>
        {
            ["author"] = "Asimov",
            ["title"] = "Fondation",
            ["ext"] = "m4b",
        };

        Renderer.Render(taxonomy.NameTemplate, fields).Should().Be("Asimov - Fondation.m4b");
    }

    [Fact]
    public void Resolve_Audiobook_WithSeries_ProducesCanonicalPlacement()
    {
        var resolver = new CanonicalPathResolver(Renderer);
        var taxonomy = DefaultTaxonomies.For(MediaCategory.Audiobook)!;
        var fields = new Dictionary<string, string?>
        {
            ["author"] = "Asimov",
            ["series"] = "Fondation",
            ["volume"] = "2",
            ["title"] = "Fondation et Empire",
            ["ext"] = "m4b",
        };

        var placement = resolver.Resolve(taxonomy, fields);

        placement.Should().NotBeNull();
        placement!.RelativeDirectory.Should().Be(@"Livres audio\Asimov\Fondation");
        placement.FileName.Should().Be("Asimov - Fondation - 02 - Fondation et Empire.m4b");
        placement.RelativePath.Should().Be(@"Livres audio\Asimov\Fondation\Asimov - Fondation - 02 - Fondation et Empire.m4b");
    }

    [Fact]
    public void Resolve_Book_WithoutSeries_ProducesCanonicalPlacement()
    {
        var resolver = new CanonicalPathResolver(Renderer);
        var taxonomy = DefaultTaxonomies.For(MediaCategory.Book)!;
        var fields = new Dictionary<string, string?>
        {
            ["author"] = "Orwell",
            ["title"] = "1984",
            ["ext"] = "epub",
        };

        var placement = resolver.Resolve(taxonomy, fields);

        placement!.RelativePath.Should().Be(@"Livres\Orwell\Orwell - 1984.epub");
    }

    [Fact]
    public async Task Repository_ReturnsDefault_WhenNotCustomized()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = new SqliteTaxonomyRepository(db.Factory);

        var taxonomy = await repository.GetAsync(MediaCategory.Audiobook, CancellationToken.None);

        taxonomy.Should().NotBeNull();
        taxonomy!.PathTemplate.Should().Be(DefaultTaxonomies.For(MediaCategory.Audiobook)!.PathTemplate);
    }

    [Fact]
    public async Task Repository_PersistsCustomTaxonomy_AsNewActiveVersion()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = new SqliteTaxonomyRepository(db.Factory);

        var custom = new CategoryTaxonomy(
            MediaCategory.Book,
            PathTemplate: "Mes Livres/{{ author }}",
            NameTemplate: "{{ title }}.{{ ext }}",
            RequiredFields: new[] { "title" });

        var version1 = await repository.SaveAsync(custom, CancellationToken.None);
        version1.Should().Be(1);

        var loaded = await repository.GetAsync(MediaCategory.Book, CancellationToken.None);
        loaded!.PathTemplate.Should().Be("Mes Livres/{{ author }}");
        loaded.RequiredFields.Should().BeEquivalentTo("title");

        // Une seconde sauvegarde crée la version 2 active ; la 1 est désactivée.
        var version2 = await repository.SaveAsync(custom with { NameTemplate = "{{ author }} - {{ title }}.{{ ext }}" }, CancellationToken.None);
        version2.Should().Be(2);
        (await repository.GetAsync(MediaCategory.Book, CancellationToken.None))!.Version.Should().Be(2);
    }

    // L'ancien template livré pour les livres audio, avant la branche {{ if chapter }}.
    private static readonly CategoryTaxonomy LegacyAudiobookDefault = new(
        MediaCategory.Audiobook,
        PathTemplate: "Livres audio/{{ author }}{{ if series }}/{{ series }}{{ end }}",
        NameTemplate: "{{ author }} - {{ if series }}{{ series }} - {{ volume | format \"00\" }} - {{ end }}{{ title }}.{{ ext }}",
        RequiredFields: new[] { "author", "title" });

    [Fact]
    public async Task Upgrade_RemovesStoredOldDefault_SoCurrentDefaultApplies()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = new SqliteTaxonomyRepository(db.Factory);

        // Simule une base ayant enregistré l'ancien défaut (cas réel rencontré).
        await repository.SaveAsync(LegacyAudiobookDefault, CancellationToken.None);

        var upgrader = new UpgradeDefaultTaxonomiesHandler(
            repository, NullLogger<UpgradeDefaultTaxonomiesHandler>.Instance);
        var upgraded = await upgrader.ExecuteAsync(CancellationToken.None);

        upgraded.Should().Be(1);
        // Plus de ligne stockée → l'app sert le défaut courant (avec la branche {{ if chapter }}).
        (await repository.GetStoredAsync(MediaCategory.Audiobook, CancellationToken.None)).Should().BeNull();
        var effective = await repository.GetAsync(MediaCategory.Audiobook, CancellationToken.None);
        effective!.NameTemplate.Should().Be(DefaultTaxonomies.For(MediaCategory.Audiobook)!.NameTemplate);
        effective.NameTemplate.Should().Contain("if chapter");
    }

    [Fact]
    public async Task Upgrade_PreservesUserCustomization()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = new SqliteTaxonomyRepository(db.Factory);

        var custom = new CategoryTaxonomy(
            MediaCategory.Audiobook,
            PathTemplate: "Mes audios/{{ author }}",
            NameTemplate: "{{ title }}.{{ ext }}",
            RequiredFields: new[] { "title" });
        await repository.SaveAsync(custom, CancellationToken.None);

        var upgrader = new UpgradeDefaultTaxonomiesHandler(
            repository, NullLogger<UpgradeDefaultTaxonomiesHandler>.Instance);
        var upgraded = await upgrader.ExecuteAsync(CancellationToken.None);

        upgraded.Should().Be(0);
        var stored = await repository.GetStoredAsync(MediaCategory.Audiobook, CancellationToken.None);
        stored!.PathTemplate.Should().Be("Mes audios/{{ author }}");
    }

    [Fact]
    public async Task Upgrade_NoStoredRows_IsNoOp()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = new SqliteTaxonomyRepository(db.Factory);

        var upgrader = new UpgradeDefaultTaxonomiesHandler(
            repository, NullLogger<UpgradeDefaultTaxonomiesHandler>.Instance);

        (await upgrader.ExecuteAsync(CancellationToken.None)).Should().Be(0);
    }
}
