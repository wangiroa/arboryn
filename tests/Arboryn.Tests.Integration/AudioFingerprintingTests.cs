using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.FileSystem;
using Arboryn.Infrastructure.Persistence;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

/// <summary>
/// Tests acoustiques (Inc 5). fpcalc n'étant pas requis ici, le calcul d'empreinte est
/// simulé par un <see cref="StubFingerprinter"/> ; le reste de la chaîne (store SQLite,
/// détection, promotion) est réel.
/// </summary>
public class AudioFingerprintingTests
{
    [Fact]
    public async Task Store_PersistsFingerprint_AndInvalidatesOnFileChange()
    {
        await using var db = await TestDatabase.CreateAsync();
        var repository = new SqliteFileInstanceRepository(db.Factory);

        var path = FilePath.From(@"C:\audio\livre.mp3");
        var id = await repository.UpsertAsync(
            new FileInstanceRecord(FileInstanceId.New(), VolumeId.Default, path,
                CanonicalName.From("livre.mp3"), Size: 100, ModifiedAt: DateTime.UnixEpoch),
            CancellationToken.None);

        IAudioFingerprintStore store = repository;
        (await store.GetWithoutFingerprintAsync(VolumeId.Default, null, CancellationToken.None))
            .Should().ContainSingle(r => r.Id == id);

        var fingerprint = new AudioFingerprint(new uint[] { 10, 20, 30, 40 });
        await store.SetAsync(id, fingerprint, CancellationToken.None);

        var fingerprinted = await store.GetFingerprintedAsync(VolumeId.Default, null, CancellationToken.None);
        fingerprinted.Should().ContainSingle();
        fingerprinted[0].Fingerprint.SubFingerprints.Should().Equal(10u, 20u, 30u, 40u);
        (await store.GetWithoutFingerprintAsync(VolumeId.Default, null, CancellationToken.None))
            .Should().BeEmpty();

        // Le fichier change (taille) → l'empreinte est invalidée au re-scan.
        await repository.UpsertAsync(
            new FileInstanceRecord(FileInstanceId.New(), VolumeId.Default, path,
                CanonicalName.From("livre.mp3"), Size: 200, ModifiedAt: DateTime.UnixEpoch),
            CancellationToken.None);

        (await store.GetFingerprintedAsync(VolumeId.Default, null, CancellationToken.None)).Should().BeEmpty();
        (await store.GetWithoutFingerprintAsync(VolumeId.Default, null, CancellationToken.None))
            .Should().ContainSingle(r => r.Id == id);
    }

    [Fact]
    public async Task ScanComputeDetectPromote_SameTrackTwoFormats_ShareOneLogicalFile()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        // Même morceau en deux formats + un morceau différent + un non-audio.
        temp.Write("livre.mp3", "x");
        temp.Write("livre.flac", "y");
        temp.Write("autre.mp3", "z");
        temp.Write("notes.txt", "pas audio");

        var repository = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var extractor = new ExtractMetadataHandler(
            metadata, Array.Empty<IContentMetadataReader>(), NullLogger<ExtractMetadataHandler>.Instance);
        var scanHandler = new ScanDirectoryHandler(
            new FileScanner(NullLogger<FileScanner>.Instance),
            repository, logicalFiles, extractor, NullLogger<ScanDirectoryHandler>.Instance);

        await scanHandler.ExecuteAsync(FilePath.From(temp.Path), VolumeId.Default);

        // 1) Empreintes (simulées) : « livre » → même empreinte ; « autre » → différente.
        var computer = new ComputeAudioFingerprintsHandler(
            repository, new StubFingerprinter(), NullLogger<ComputeAudioFingerprintsHandler>.Instance);
        (await computer.ExecuteAsync(VolumeId.Default)).Should().Be(3); // le .txt est ignoré

        // 2) Détection acoustique.
        var detector = new DetectAudioDuplicatesHandler(repository);
        var groups = await detector.ExecuteAsync(VolumeId.Default);
        groups.Should().HaveCount(1);
        groups[0].Members.Select(m => m.CanonicalName.Value)
            .Should().BeEquivalentTo("livre.mp3", "livre.flac");

        // 3) Promotion → même LogicalFile à signature chromaprint.
        var promoter = new PromoteAudioHandler(repository, logicalFiles, repository);
        (await promoter.ExecuteAsync(VolumeId.Default)).Should().Be(1);

        await using var connection = await db.Factory.OpenAsync();
        var sharedLogicalIds = (await connection.QueryAsync<string>(
            "SELECT DISTINCT logical_file_id FROM file_instances WHERE relative_path IN (@A, @B);",
            new
            {
                A = Path.Combine(temp.Path, "livre.mp3"),
                B = Path.Combine(temp.Path, "livre.flac"),
            })).ToList();
        sharedLogicalIds.Should().HaveCount(1);

        var signatureKind = await connection.ExecuteScalarAsync<string>(
            "SELECT content_signature_kind FROM logical_files WHERE id = @Id;",
            new { Id = sharedLogicalIds.Single() });
        signatureKind.Should().Be("chromaprint");
    }

    /// <summary>
    /// Empreinte simulée : tous les fichiers contenant « livre » partagent la même suite,
    /// les autres en ont une distincte. Évite la dépendance à fpcalc dans les tests.
    /// </summary>
    private sealed class StubFingerprinter : IAudioFingerprinter
    {
        public Task<AudioFingerprint?> ComputeAsync(FilePath path, CancellationToken cancellationToken)
        {
            var seed = path.FileName.Contains("livre", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
            return Task.FromResult<AudioFingerprint?>(Sequence(seed, length: 250));
        }

        private static AudioFingerprint Sequence(int seed, int length)
        {
            var subs = new uint[length];
            var state = (uint)seed * 2654435761u + 1u;
            for (var i = 0; i < length; i++)
            {
                state = (state * 1664525u) + 1013904223u;
                subs[i] = state;
            }

            return new AudioFingerprint(subs);
        }
    }
}
