using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Triage;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.FileSystem;
using Arboryn.Infrastructure.Persistence;
using Arboryn.Infrastructure.Templates;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

/// <summary>
/// Inc 7 — triage de bout en bout : préparation (pré-remplissage via texte extrait), puis
/// application (placement canonique des documents officiels), apprentissage des corrections,
/// et annulation. Les adaptateurs natifs (texte/OCR/miniature) sont remplacés par des stubs.
/// </summary>
public class TriageWorkflowTests
{
    [Fact]
    public async Task Prepare_PrefillsFields_FromExtractedText()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        var instances = new SqliteFileInstanceRepository(db.Factory);
        var triage = new SqliteTriageRepository(db.Factory);

        var id = await IndexDocument(temp, instances, @"incoming\scan1.pdf");

        var text = new StubTextExtractor("EDF\nFacture d'électricité\nParis, le 05/06/2024\nMontant 42 €");
        var prepare = new PrepareTriageHandler(
            instances, triage, new[] { (IDocumentTextExtractor)text },
            new StubOcr(available: false), new StubThumbnail(),
            NullLogger<PrepareTriageHandler>.Instance);

        var result = await prepare.ExecuteAsync(
            VolumeId.Default, FilePath.From(temp.Path), Path.Combine(temp.Path, ".thumbs"));

        result.Candidates.Should().HaveCount(1);
        var candidate = result.Candidates[0];
        candidate.InstanceId.Value.Should().Be(id.Value);
        candidate.Extraction.Object.Value.Should().Be("Facture");
        candidate.Extraction.Date.Value.Should().Be("202406");
        candidate.Extraction.Source.Value.Should().Be("EDF");

        // Les patterns par défaut ont été semés à la préparation.
        (await triage.GetActivePatternsAsync(CancellationToken.None)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Apply_PlacesDocuments_RecordsCorrection_LearnsPattern_AndUndoes()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        var instances = new SqliteFileInstanceRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var taxonomies = new SqliteTaxonomyRepository(db.Factory);
        var triage = new SqliteTriageRepository(db.Factory);
        var journal = new SqliteOperationJournal(db.Factory);
        var mover = new FileSystemMover();
        var resolver = new CanonicalPathResolver(new ScribanTemplateRenderer());
        var executor = new ExecuteUniformizationHandler(
            mover, instances, journal, NullLogger<ExecuteUniformizationHandler>.Instance);

        var id1 = await IndexDocument(temp, instances, @"incoming\a.pdf");
        var id2 = await IndexDocument(temp, instances, @"incoming\b.pdf");

        var decisions = new[]
        {
            // Une correction de source (pré-rempli « Cabinet » → corrigé « Foncia »).
            new TriageDecision(
                id1, FilePath.From(Path.Combine(temp.Path, @"incoming\a.pdf")),
                Source: "Foncia", Object: "Appel de fonds", Date: "202403", Subcategory: "Investissements",
                Snippet: "…Cabinet Foncia…", OriginalSource: "Cabinet", OriginalObject: "Appel de fonds"),
            new TriageDecision(
                id2, FilePath.From(Path.Combine(temp.Path, @"incoming\b.pdf")),
                Source: "EDF", Object: "Facture", Date: "202406", Subcategory: "Logement",
                Snippet: "…EDF…", OriginalSource: "EDF", OriginalObject: "Facture"),
        };

        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var apply = new ApplyTriageHandler(
            metadata, taxonomies, triage, logicalFiles, resolver, executor, mover,
            NullLogger<ApplyTriageHandler>.Instance);

        var result = await apply.ExecuteAsync(decisions, FilePath.From(temp.Path));

        result.Applied.Should().Be(2);
        result.Failed.Should().Be(0);

        var placed1 = Path.Combine(temp.Path, @"Documents officiels\Investissements\[Foncia] - Appel de fonds - 202403.pdf");
        var placed2 = Path.Combine(temp.Path, @"Documents officiels\Logement\[EDF] - Facture - 202406.pdf");
        File.Exists(placed1).Should().BeTrue();
        File.Exists(placed2).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, @"incoming\a.pdf")).Should().BeFalse();

        // Les champs sont stockés en métadonnées (source « triage »).
        await using (var connection = await db.Factory.OpenAsync())
        {
            var src = await connection.ExecuteScalarAsync<string>(
                "SELECT value FROM file_metadata WHERE file_instance_id = @Id AND key = 'source';",
                new { Id = id1.Value });
            src.Should().Be("Foncia");
        }

        // La correction de source a été enregistrée puis dérivée en pattern par l'apprentissage.
        var learn = new LearnTriagePatternsHandler(triage, NullLogger<LearnTriagePatternsHandler>.Instance);
        var learned = await learn.ExecuteAsync();
        learned.Should().Be(1);

        var patterns = await triage.GetActivePatternsAsync(CancellationToken.None);
        patterns.Should().Contain(p => p.LearnedFromUser && p.Template == "Foncia");

        // Annulation : les documents reviennent à incoming/.
        var undo = new UndoUniformizationHandler(
            journal, mover, instances, NullLogger<UndoUniformizationHandler>.Instance);
        var undoResult = await undo.ExecuteAsync();

        undoResult.HadBatch.Should().BeTrue();
        undoResult.Restored.Should().Be(2);
        File.Exists(Path.Combine(temp.Path, @"incoming\a.pdf")).Should().BeTrue();
        File.Exists(placed1).Should().BeFalse();
    }

    [Fact]
    public async Task Apply_FlipsLogicalCategory_SoSubsequentNormalizeIsNoOp()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        var instances = new SqliteFileInstanceRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var taxonomies = new SqliteTaxonomyRepository(db.Factory);
        var triage = new SqliteTriageRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var journal = new SqliteOperationJournal(db.Factory);
        var mover = new FileSystemMover();
        var resolver = new CanonicalPathResolver(new ScribanTemplateRenderer());
        var executor = new ExecuteUniformizationHandler(
            mover, instances, journal, NullLogger<ExecuteUniformizationHandler>.Instance);

        var id = await IndexDocument(temp, instances, @"incoming\facture.pdf");
        // Rattache l'instance à un LogicalFile (catégorie « unknown » par défaut), comme après un scan.
        await logicalFiles.BackfillUnattachedAsync(CancellationToken.None);

        var decision = new TriageDecision(
            id, FilePath.From(Path.Combine(temp.Path, @"incoming\facture.pdf")),
            Source: "EDF", Object: "Facture", Date: "202403", Subcategory: "Logement",
            Snippet: "EDF facture", OriginalSource: "EDF", OriginalObject: "Facture");

        var apply = new ApplyTriageHandler(
            metadata, taxonomies, triage, logicalFiles, resolver, executor, mover,
            NullLogger<ApplyTriageHandler>.Instance);
        await apply.ExecuteAsync(new[] { decision }, FilePath.From(temp.Path));

        // La catégorie du LogicalFile est passée à « official_document ».
        await using (var connection = await db.Factory.OpenAsync())
        {
            var category = await connection.ExecuteScalarAsync<string>(
                "SELECT category FROM logical_files LIMIT 1;");
            category.Should().Be("official_document");
        }

        // Un passage d'uniformisation ne propose plus rien : le document est déjà à son
        // emplacement canonique de document officiel (et non « PDF divers »).
        var planner = new PlanUniformizationHandler(instances, metadata, taxonomies, resolver, mover);
        var plan = await planner.ExecuteAsync(VolumeId.Default, FilePath.From(temp.Path));

        plan.Operations.Should().BeEmpty();
        plan.AlreadyCanonical.Should().Be(1);
        File.Exists(Path.Combine(temp.Path, @"Documents officiels\Logement\[EDF] - Facture - 202403.pdf"))
            .Should().BeTrue();
    }

    private static async Task<FileInstanceId> IndexDocument(
        TempDir temp, SqliteFileInstanceRepository instances, string relativePath)
    {
        temp.Write(relativePath, "pdf-bytes");
        var absolute = Path.Combine(temp.Path, relativePath);
        return await instances.UpsertAsync(
            new FileInstanceRecord(
                FileInstanceId.New(), VolumeId.Default, FilePath.From(absolute),
                CanonicalName.From(Path.GetFileName(absolute)), Size: 9, ModifiedAt: DateTime.UnixEpoch),
            CancellationToken.None);
    }

    private sealed class StubTextExtractor : IDocumentTextExtractor
    {
        private readonly string _text;
        public StubTextExtractor(string text) => _text = text;
        public bool CanExtract(string extension) => extension.TrimStart('.').Equals("pdf", StringComparison.OrdinalIgnoreCase);
        public Task<string?> ExtractFirstPageTextAsync(FilePath path, CancellationToken cancellationToken)
            => Task.FromResult<string?>(_text);
    }

    private sealed class StubOcr : IOcrEngine
    {
        public StubOcr(bool available) => IsAvailable = available;
        public bool IsAvailable { get; }
        public Task<string?> RecognizeAsync(FilePath imagePath, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);
    }

    private sealed class StubThumbnail : IDocumentThumbnailRenderer
    {
        public bool CanRender(string extension) => false;
        public Task<FilePath?> RenderFirstPageAsync(
            FilePath source, string outputDirectory, int maxWidth, CancellationToken cancellationToken)
            => Task.FromResult<FilePath?>(null);
    }
}
