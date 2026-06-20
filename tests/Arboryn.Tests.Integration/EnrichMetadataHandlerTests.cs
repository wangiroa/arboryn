using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enrichment;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

/// <summary>
/// Inc 8 — orchestration de l'enrichissement : auto-application au-dessus du seuil, mise en
/// cache (pas de second appel réseau), respect du mode local-only, et candidats sous le seuil.
/// </summary>
public class EnrichMetadataHandlerTests
{
    [Fact]
    public async Task Enrich_AutoAppliesHighConfidence_ThenServesFromCache()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (instances, metadata, settings, cache, candidates) = Repos(db);

        var id = await SeedBookAsync(instances, metadata);
        await settings.SetAsync("online_mode_enabled", "true", CancellationToken.None);

        // Champs hors liste blanche du livre → n'altèrent pas la requête entre deux appels.
        var provider = new FakeProvider(MediaCategory.Book, new EnrichmentResult(
            "fake",
            new Dictionary<string, string> { [MetadataKeys.Subtitle] = "Sous-titre", [MetadataKeys.Genre] = "SF" },
            0.95, EnrichmentMatchKind.Identifier));

        var handler = Handler(provider, cache, metadata, settings, candidates);

        var first = await handler.ExecuteAsync(id, MediaCategory.Book);
        first.AppliedFields.Should().Be(2);
        first.NetworkUsed.Should().BeTrue();
        provider.Calls.Should().Be(1);

        var stored = await metadata.GetForInstanceAsync(id, CancellationToken.None);
        stored.Should().Contain(e => e.Key == MetadataKeys.Subtitle
            && e.Source == MetadataSources.Online("fake") && e.Value == "Sous-titre");

        // Deuxième passage : la réponse vient du cache, aucun nouvel appel réseau.
        var second = await handler.ExecuteAsync(id, MediaCategory.Book);
        second.NetworkUsed.Should().BeFalse();
        provider.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Enrich_LocalOnly_MakesNoNetworkCall()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (instances, metadata, settings, cache, candidates) = Repos(db);
        var id = await SeedBookAsync(instances, metadata);
        // online_mode_enabled reste à 'false' (valeur seedée par défaut).

        var provider = new FakeProvider(MediaCategory.Book, new EnrichmentResult(
            "fake", new Dictionary<string, string> { [MetadataKeys.Genre] = "SF" }, 0.95, EnrichmentMatchKind.Identifier));
        var handler = Handler(provider, cache, metadata, settings, candidates);

        var outcome = await handler.ExecuteAsync(id, MediaCategory.Book);

        outcome.NetworkUsed.Should().BeFalse();
        outcome.AppliedFields.Should().Be(0);
        provider.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Enrich_BelowThreshold_ReturnsCandidate_WithoutApplying()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (instances, metadata, settings, cache, candidates) = Repos(db);
        var id = await SeedBookAsync(instances, metadata);
        await settings.SetAsync("online_mode_enabled", "true", CancellationToken.None);

        var provider = new FakeProvider(MediaCategory.Book, new EnrichmentResult(
            "fake", new Dictionary<string, string> { [MetadataKeys.Genre] = "SF" }, 0.7, EnrichmentMatchKind.Fuzzy));
        var handler = Handler(provider, cache, metadata, settings, candidates);

        var outcome = await handler.ExecuteAsync(id, MediaCategory.Book);

        outcome.AppliedFields.Should().Be(0);
        outcome.Candidates.Should().ContainSingle(c => c.Key == MetadataKeys.Genre && c.Value == "SF");
        var stored = await metadata.GetForInstanceAsync(id, CancellationToken.None);
        stored.Should().NotContain(e => e.Source == MetadataSources.Online("fake"));

        // Le candidat sous le seuil est désormais persisté pour la révision utilisateur.
        var pending = await candidates.GetPendingAsync(CancellationToken.None);
        pending.Should().ContainSingle(c => c.Key == MetadataKeys.Genre && c.Value == "SF" && c.Provider == "fake");
    }

    private static (SqliteFileInstanceRepository, SqliteFileMetadataRepository, SqliteSettingsRepository, SqliteApiCache, SqliteEnrichmentCandidateRepository) Repos(TestDatabase db)
        => (new SqliteFileInstanceRepository(db.Factory), new SqliteFileMetadataRepository(db.Factory),
            new SqliteSettingsRepository(db.Factory), new SqliteApiCache(db.Factory),
            new SqliteEnrichmentCandidateRepository(db.Factory));

    private static EnrichMetadataHandler Handler(
        IMetadataProvider provider, IApiCache cache, IFileMetadataRepository metadata, ISettingsRepository settings,
        IEnrichmentCandidateRepository candidates)
        => new(new[] { provider }, cache, metadata, candidates, settings, NullLogger<EnrichMetadataHandler>.Instance);

    private static async Task<FileInstanceId> SeedBookAsync(
        SqliteFileInstanceRepository instances, SqliteFileMetadataRepository metadata)
    {
        var id = await instances.UpsertAsync(
            new FileInstanceRecord(
                FileInstanceId.New(), VolumeId.Default, FilePath.From(@"C:\lib\book.epub"),
                CanonicalName.From("book.epub"), Size: 100, ModifiedAt: DateTime.UnixEpoch),
            CancellationToken.None);

        var now = DateTime.UtcNow;
        await metadata.UpsertAsync(new MetadataEntry(id, MetadataKeys.Isbn, "9782070612888", MetadataSources.Filename, 0.5, now), CancellationToken.None);
        await metadata.UpsertAsync(new MetadataEntry(id, MetadataKeys.Title, "Fondation", MetadataSources.Filename, 0.5, now), CancellationToken.None);
        await metadata.UpsertAsync(new MetadataEntry(id, MetadataKeys.Author, "Asimov", MetadataSources.Filename, 0.5, now), CancellationToken.None);
        return id;
    }

    private sealed class FakeProvider : IMetadataProvider
    {
        private readonly MediaCategory _category;
        private readonly EnrichmentResult? _result;

        public FakeProvider(MediaCategory category, EnrichmentResult? result)
        {
            _category = category;
            _result = result;
        }

        public int Calls { get; private set; }

        public string Name => "fake";

        public bool CanEnrich(MediaCategory category) => category == _category;

        public bool IsConfigured => true;

        public Task<EnrichmentResult?> QueryAsync(EnrichmentQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }
}
