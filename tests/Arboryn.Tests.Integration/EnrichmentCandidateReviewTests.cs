using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

/// <summary>
/// Suivi Inc 8 — révision des candidats d'enrichissement : persistance, acceptation (écriture en
/// <c>file_metadata</c> + retrait de la liste), rejet, et non-résurrection d'un candidat rejeté
/// lors d'un re-enrichissement proposant la même valeur.
/// </summary>
public class EnrichmentCandidateReviewTests
{
    [Fact]
    public async Task Accept_WritesMetadata_AndClearsPending()
    {
        await using var db = await TestDatabase.CreateAsync();
        var candidates = new SqliteEnrichmentCandidateRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var id = await SeedInstanceAsync(db);

        await candidates.UpsertAsync(new EnrichmentCandidateRecord(
            "c1", id, "openlibrary", "publisher", "Gallimard", 0.7, EnrichmentCandidateStatus.Pending),
            CancellationToken.None);

        var handler = new ReviewEnrichmentCandidatesHandler(
            candidates, metadata, NullLogger<ReviewEnrichmentCandidatesHandler>.Instance);

        (await handler.AcceptAsync("c1")).Should().BeTrue();

        var stored = await metadata.GetForInstanceAsync(id, CancellationToken.None);
        stored.Should().Contain(e => e.Key == "publisher" && e.Value == "Gallimard"
            && e.Source == MetadataSources.Online("openlibrary"));
        (await candidates.CountPendingAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task Reject_MarksDecided_WithoutWritingMetadata()
    {
        await using var db = await TestDatabase.CreateAsync();
        var candidates = new SqliteEnrichmentCandidateRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var id = await SeedInstanceAsync(db);

        await candidates.UpsertAsync(new EnrichmentCandidateRecord(
            "c1", id, "tmdb", "year", "1999", 0.6, EnrichmentCandidateStatus.Pending), CancellationToken.None);

        var handler = new ReviewEnrichmentCandidatesHandler(
            candidates, metadata, NullLogger<ReviewEnrichmentCandidatesHandler>.Instance);

        (await handler.RejectAsync("c1")).Should().BeTrue();

        (await candidates.CountPendingAsync(CancellationToken.None)).Should().Be(0);
        var stored = await metadata.GetForInstanceAsync(id, CancellationToken.None);
        stored.Should().NotContain(e => e.Source == MetadataSources.Online("tmdb"));
    }

    [Fact]
    public async Task Reupsert_SameValue_DoesNotResurrectRejectedCandidate()
    {
        await using var db = await TestDatabase.CreateAsync();
        var candidates = new SqliteEnrichmentCandidateRepository(db.Factory);
        var id = await SeedInstanceAsync(db);

        await candidates.UpsertAsync(new EnrichmentCandidateRecord(
            "c1", id, "googlebooks", "publisher", "Dargaud", 0.7, EnrichmentCandidateStatus.Pending),
            CancellationToken.None);
        await candidates.SetStatusAsync("c1", EnrichmentCandidateStatus.Rejected, CancellationToken.None);

        // Re-enrichissement : même (instance, provider, clé) et même valeur → reste rejeté.
        await candidates.UpsertAsync(new EnrichmentCandidateRecord(
            "c2", id, "googlebooks", "publisher", "Dargaud", 0.72, EnrichmentCandidateStatus.Pending),
            CancellationToken.None);

        (await candidates.CountPendingAsync(CancellationToken.None)).Should().Be(0);

        // Mais une valeur différente rouvre la décision.
        await candidates.UpsertAsync(new EnrichmentCandidateRecord(
            "c3", id, "googlebooks", "publisher", "Glénat", 0.71, EnrichmentCandidateStatus.Pending),
            CancellationToken.None);
        var pending = await candidates.GetPendingAsync(CancellationToken.None);
        pending.Should().ContainSingle(c => c.Value == "Glénat");
    }

    private static async Task<FileInstanceId> SeedInstanceAsync(TestDatabase db)
    {
        var instances = new SqliteFileInstanceRepository(db.Factory);
        return await instances.UpsertAsync(
            new FileInstanceRecord(
                FileInstanceId.New(), VolumeId.Default, FilePath.From(@"C:\lib\doc.epub"),
                CanonicalName.From("doc.epub"), Size: 100, ModifiedAt: DateTime.UnixEpoch),
            CancellationToken.None);
    }
}
