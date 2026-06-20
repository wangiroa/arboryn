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
    public async Task Execute_OnlySelectedSubset_MovesSelected_AndLeavesDeselectedInPlace()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        var instances = new SqliteFileInstanceRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var taxonomies = new SqliteTaxonomyRepository(db.Factory);
        var resolver = new CanonicalPathResolver(new ScribanTemplateRenderer());
        var mover = new FileSystemMover();
        var journal = new SqliteOperationJournal(db.Factory);

        var raw1 = await IndexAudiobook(temp, instances, metadata, @"incoming\raw1.m4b", "Asimov", "Fondation");
        var raw2 = await IndexAudiobook(temp, instances, metadata, @"incoming\raw2.m4b", "Asimov", "Les Robots");

        var planner = new PlanUniformizationHandler(instances, metadata, taxonomies, resolver, mover);
        var plan = await planner.ExecuteAsync(VolumeId.Default, FilePath.From(temp.Path));
        plan.Operations.Should().HaveCount(2);

        // L'utilisateur ne coche que l'opération de raw1 (sélection individuelle).
        var selected = plan.Operations.Where(o => o.Id.Value == raw1.Value).ToList();
        selected.Should().HaveCount(1);

        var executor = new ExecuteUniformizationHandler(
            mover, instances, journal, NullLogger<ExecuteUniformizationHandler>.Instance);
        var result = await executor.ExecuteAsync(selected);

        result.Moved.Should().Be(1);
        result.Failed.Should().Be(0);

        // Seul raw1 a bougé ; raw2 (décoché) reste à sa place d'origine.
        File.Exists(Path.Combine(temp.Path, @"Livres audio\Asimov\Asimov - Fondation.m4b")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, @"incoming\raw1.m4b")).Should().BeFalse();
        File.Exists(Path.Combine(temp.Path, @"incoming\raw2.m4b")).Should().BeTrue();
        File.Exists(Path.Combine(temp.Path, @"Livres audio\Asimov\Asimov - Les Robots.m4b")).Should().BeFalse();

        // Le catalogue ne reflète le déplacement que pour raw1.
        await using var connection = await db.Factory.OpenAsync();
        var path2 = await connection.ExecuteScalarAsync<string>(
            "SELECT relative_path FROM file_instances WHERE id = @Id;", new { Id = raw2.Value });
        path2.Should().EndWith(@"incoming\raw2.m4b");
    }

    [Fact]
    public async Task Rebuild_OverSubset_FreesCollisionSuffix()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        var instances = new SqliteFileInstanceRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var taxonomies = new SqliteTaxonomyRepository(db.Factory);
        var resolver = new CanonicalPathResolver(new ScribanTemplateRenderer());
        var mover = new FileSystemMover();

        // Deux fichiers en collision → l'un est suffixé « (2) » dans le plan complet.
        await IndexAudiobook(temp, instances, metadata, @"incoming\raw1.m4b", "Asimov", "Fondation");
        var raw2 = await IndexAudiobook(temp, instances, metadata, @"incoming\raw2.m4b", "Asimov", "Fondation");

        var planner = new PlanUniformizationHandler(instances, metadata, taxonomies, resolver, mover);
        var plan = await planner.ExecuteAsync(VolumeId.Default, FilePath.From(temp.Path));
        plan.Operations.Should().HaveCount(2);
        plan.Targets.Should().NotBeNull();
        plan.Operations.Select(o => o.NewPath.Value).Should()
            .Contain(p => p.EndsWith(@"Asimov - Fondation (2).m4b"));

        // L'utilisateur ne garde que raw2 : recalculé seul, il récupère le nom sans suffixe.
        var selected = plan.Targets!.Where(t => t.Instance.Id.Value == raw2.Value).ToList();
        var rebuilt = planner.RebuildOperations(selected, FilePath.From(temp.Path));

        rebuilt.Should().ContainSingle();
        rebuilt[0].NewPath.Value.Should().Be(
            Path.Combine(temp.Path, @"Livres audio\Asimov\Asimov - Fondation.m4b"));
        rebuilt[0].NewPath.Value.Should().NotContain("(2)");
    }

    [Fact]
    public async Task Plan_DerivesWorkTitleFromDirectory_AndNumbersChapters_WithConstantWidth()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        var instances = new SqliteFileInstanceRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var taxonomies = new SqliteTaxonomyRepository(db.Factory);
        var resolver = new CanonicalPathResolver(new ScribanTemplateRenderer());
        var mover = new FileSystemMover();

        // Livre audio découpé en 10 pistes : le titre est porté par le dossier, pas le fichier
        // (« 001 chapitre 1.mp3 » n'identifie qu'une position). On vérifie la numérotation à
        // largeur constante, démarrant à 01 (pas de « rien → (2) »).
        const string work = "Histoire totale de la seconde guerre mondiale";
        const string folder = work + " - Olivier Wieviorka";
        var ids = new Dictionary<int, FileInstanceId>();
        for (var n = 1; n <= 10; n++)
        {
            // Tag Title trompeur : il porte le titre de l'œuvre, pas un titre de chapitre
            // (cas réel). Seul le nom de fichier positionnel distingue les pistes.
            ids[n] = await IndexAudiobook(
                temp, instances, metadata,
                $@"{folder}\{n:000} chapitre {n}.mp3", author: "Olivier Wieviorka", title: work);
        }

        var planner = new PlanUniformizationHandler(instances, metadata, taxonomies, resolver, mover);
        var plan = await planner.ExecuteAsync(VolumeId.Default, FilePath.From(temp.Path));

        plan.Operations.Should().HaveCount(10);
        var byId = plan.Operations.ToDictionary(o => o.Id.Value, o => o.NewPath.Value);

        const string targetDir = @"Livres audio\Olivier Wieviorka";
        byId[ids[1].Value].Should().Be(Path.Combine(
            temp.Path, targetDir, "Histoire totale de la seconde guerre mondiale - 01.mp3"));
        byId[ids[10].Value].Should().Be(Path.Combine(
            temp.Path, targetDir, "Histoire totale de la seconde guerre mondiale - 10.mp3"));

        // Aucune désambiguïsation « (2) » : chaque chapitre a un nom distinct dès le départ.
        byId.Values.Should().OnlyContain(p => !p.Contains("(2)"));
    }

    [Fact]
    public async Task Plan_NumbersComicSeries_FromDirectory()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        var instances = new SqliteFileInstanceRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var taxonomies = new SqliteTaxonomyRepository(db.Factory);
        var resolver = new CanonicalPathResolver(new ScribanTemplateRenderer());
        var mover = new FileSystemMover();

        // Série BD découpée en tomes nus (« 01.cbz »…) : titre = dossier, pas d'auteur requis.
        var id1 = await IndexComic(temp, instances, @"XIII\01.cbz");
        await IndexComic(temp, instances, @"XIII\02.cbz");

        var planner = new PlanUniformizationHandler(instances, metadata, taxonomies, resolver, mover);
        var plan = await planner.ExecuteAsync(VolumeId.Default, FilePath.From(temp.Path));

        plan.Operations.Should().HaveCount(2);
        var byId = plan.Operations.ToDictionary(o => o.Id.Value, o => o.NewPath.Value);
        byId[id1.Value].Should().Be(Path.Combine(temp.Path, @"Bandes dessinées\XIII\XIII - 1.cbz"));
    }

    private static async Task<FileInstanceId> IndexComic(
        TempDir temp, SqliteFileInstanceRepository instances, string relativePath)
    {
        temp.Write(relativePath, "comic-bytes");
        var absolute = Path.Combine(temp.Path, relativePath);
        return await instances.UpsertAsync(
            new FileInstanceRecord(
                FileInstanceId.New(), VolumeId.Default, FilePath.From(absolute),
                CanonicalName.From(Path.GetFileName(absolute)), Size: 12, ModifiedAt: DateTime.UnixEpoch),
            CancellationToken.None);
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
