using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
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
/// Inc 6 — uniformisation de bout en bout : planification (chemins canoniques + collisions),
/// exécution (déplacements réels + mise à jour du catalogue + journal) et annulation complète.
/// </summary>
public class UniformizationTests
{
    [Fact]
    public async Task PlanExecuteUndo_UniformizesAudiobooks_WithoutCollision_AndUndoesFully()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        var instances = new SqliteFileInstanceRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var taxonomies = new SqliteTaxonomyRepository(db.Factory);
        var resolver = new CanonicalPathResolver(new ScribanTemplateRenderer());
        var mover = new FileSystemMover();
        var journal = new SqliteOperationJournal(db.Factory);

        // Trois fichiers « bruts » sous incoming/. Deux résolvent vers le même nom (collision).
        var raw1 = await IndexAudiobook(temp, instances, metadata, @"incoming\raw1.m4b", "Asimov", "Fondation");
        var raw2 = await IndexAudiobook(temp, instances, metadata, @"incoming\raw2.m4b", "Asimov", "Fondation");
        var raw3 = await IndexAudiobook(temp, instances, metadata, @"incoming\raw3.m4b", "Asimov", "Fondation et Empire");

        // 1) Planification.
        var planner = new PlanUniformizationHandler(instances, metadata, taxonomies, resolver, mover);
        var plan = await planner.ExecuteAsync(VolumeId.Default, FilePath.From(temp.Path));

        plan.Operations.Should().HaveCount(3);
        plan.Skipped.Should().Be(0);
        var byId = plan.Operations.ToDictionary(o => o.Id.Value, o => o.NewPath.Value);
        byId[raw1.Value].Should().Be(Path.Combine(temp.Path, @"Livres audio\Asimov\Asimov - Fondation.m4b"));
        byId[raw2.Value].Should().Be(Path.Combine(temp.Path, @"Livres audio\Asimov\Asimov - Fondation (2).m4b"));
        byId[raw3.Value].Should().Be(Path.Combine(temp.Path, @"Livres audio\Asimov\Asimov - Fondation et Empire.m4b"));

        // 2) Exécution : déplacements réels.
        var executor = new ExecuteUniformizationHandler(
            mover, instances, journal, NullLogger<ExecuteUniformizationHandler>.Instance);
        var result = await executor.ExecuteAsync(plan);

        result.Moved.Should().Be(3);
        result.Failed.Should().Be(0);

        File.Exists(Path.Combine(temp.Path, @"Livres audio\Asimov\Asimov - Fondation.m4b")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, @"Livres audio\Asimov\Asimov - Fondation (2).m4b")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, @"Livres audio\Asimov\Asimov - Fondation et Empire.m4b")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, @"incoming\raw1.m4b")).Should().BeFalse();

        // Le catalogue reflète les nouveaux chemins.
        await using (var connection = await db.Factory.OpenAsync())
        {
            var path1 = await connection.ExecuteScalarAsync<string>(
                "SELECT relative_path FROM file_instances WHERE id = @Id;", new { Id = raw1.Value });
            path1.Should().EndWith(@"Livres audio\Asimov\Asimov - Fondation.m4b");
        }

        // 3) Annulation complète : tout revient à l'état initial.
        var undo = new UndoUniformizationHandler(
            journal, mover, instances, NullLogger<UndoUniformizationHandler>.Instance);
        var undoResult = await undo.ExecuteAsync();

        undoResult.HadBatch.Should().BeTrue();
        undoResult.Restored.Should().Be(3);
        undoResult.Failed.Should().Be(0);

        File.Exists(Path.Combine(temp.Path, @"incoming\raw1.m4b")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, @"incoming\raw2.m4b")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, @"incoming\raw3.m4b")).Should().BeTrue();
        Directory.GetFiles(Path.Combine(temp.Path, @"Livres audio\Asimov")).Should().BeEmpty();

        await using (var connection = await db.Factory.OpenAsync())
        {
            var path1 = await connection.ExecuteScalarAsync<string>(
                "SELECT relative_path FROM file_instances WHERE id = @Id;", new { Id = raw1.Value });
            path1.Should().EndWith(@"incoming\raw1.m4b");
        }
    }

    [Fact]
    public async Task Plan_SkipsFilesMissingRequiredMetadata()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        var instances = new SqliteFileInstanceRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var taxonomies = new SqliteTaxonomyRepository(db.Factory);
        var resolver = new CanonicalPathResolver(new ScribanTemplateRenderer());
        var mover = new FileSystemMover();

        // Auteur sans titre → champ requis manquant → ignoré.
        await IndexAudiobook(temp, instances, metadata, @"incoming\partial.m4b", author: "Asimov", title: null);

        var planner = new PlanUniformizationHandler(instances, metadata, taxonomies, resolver, mover);
        var plan = await planner.ExecuteAsync(VolumeId.Default, FilePath.From(temp.Path));

        plan.Operations.Should().BeEmpty();
        plan.Skipped.Should().Be(1);
    }

    private static async Task<FileInstanceId> IndexAudiobook(
        TempDir temp, SqliteFileInstanceRepository instances, SqliteFileMetadataRepository metadata,
        string relativePath, string author, string? title)
    {
        temp.Write(relativePath, "audio-bytes");
        var absolute = Path.Combine(temp.Path, relativePath);

        var id = await instances.UpsertAsync(
            new FileInstanceRecord(
                FileInstanceId.New(), VolumeId.Default, FilePath.From(absolute),
                CanonicalName.From(Path.GetFileName(absolute)), Size: 11, ModifiedAt: DateTime.UnixEpoch),
            CancellationToken.None);

        var now = DateTime.UtcNow;
        await metadata.UpsertAsync(
            new MetadataEntry(id, MetadataKeys.Author, author, MetadataSources.User, 1.0, now), CancellationToken.None);
        if (title is not null)
        {
            await metadata.UpsertAsync(
                new MetadataEntry(id, MetadataKeys.Title, title, MetadataSources.User, 1.0, now), CancellationToken.None);
        }

        return id;
    }
}
