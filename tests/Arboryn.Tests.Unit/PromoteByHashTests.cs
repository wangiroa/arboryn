using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class PromoteByHashTests
{
    [Fact]
    public async Task Promote_LinksAllMembersToSameSha256LogicalFile()
    {
        var logicalFiles = new FakeLogicalFileRepository();
        var linker = new FakeLinker();
        var handler = new PromoteByHashHandler(logicalFiles, linker);

        var hash = Sha256.FromHex(new string('a', 64));
        var idA = FileInstanceId.New();
        var idB = FileInstanceId.New();
        var groups = new[]
        {
            new HashGroup(hash, new[]
            {
                new HashTarget(idA, FilePath.From(@"C:\1\file.pdf")),
                new HashTarget(idB, FilePath.From(@"C:\2\file.pdf")),
            }),
        };

        await handler.ExecuteAsync(groups);

        logicalFiles.BySignature.Should().ContainKey(ContentSignature.FromSha256(hash).ToString());
        var lfId = logicalFiles.BySignature[ContentSignature.FromSha256(hash).ToString()].Id;
        linker.Attachments[idA.Value].Should().Be(lfId);
        linker.Attachments[idB.Value].Should().Be(lfId);
        logicalFiles.OrphansDeletedCount.Should().Be(1);
    }

    [Fact]
    public async Task Promote_ReusesExistingSha256LogicalFile()
    {
        var existingHash = Sha256.FromHex(new string('b', 64));
        var existing = new LogicalFile(
            LogicalFileId.New(),
            MediaCategory.Unknown,
            ContentSignature.FromSha256(existingHash),
            DateTime.UtcNow,
            DateTime.UtcNow);

        var logicalFiles = new FakeLogicalFileRepository();
        logicalFiles.BySignature[existing.Signature.ToString()] = existing;
        var linker = new FakeLinker();
        var handler = new PromoteByHashHandler(logicalFiles, linker);

        var id = FileInstanceId.New();
        await handler.ExecuteAsync(new[]
        {
            new HashGroup(existingHash, new[] { new HashTarget(id, FilePath.From(@"C:\x.pdf")) }),
        });

        linker.Attachments[id.Value].Should().Be(existing.Id);
        logicalFiles.BySignature.Should().HaveCount(1, "aucun nouveau LF créé");
    }

    [Fact]
    public async Task Promote_Empty_DoesNothing()
    {
        var logicalFiles = new FakeLogicalFileRepository();
        var linker = new FakeLinker();
        var handler = new PromoteByHashHandler(logicalFiles, linker);

        await handler.ExecuteAsync(Array.Empty<HashGroup>());

        linker.Attachments.Should().BeEmpty();
        logicalFiles.OrphansDeletedCount.Should().Be(0);
    }

    private sealed class FakeLogicalFileRepository : ILogicalFileRepository
    {
        public Dictionary<string, LogicalFile> BySignature { get; } = new();
        public int OrphansDeletedCount { get; private set; }

        public Task<LogicalFile?> FindBySignatureAsync(ContentSignature signature, CancellationToken cancellationToken)
            => Task.FromResult(BySignature.TryGetValue(signature.ToString(), out var v) ? v : null);

        public Task UpsertAsync(LogicalFile logicalFile, CancellationToken cancellationToken)
        {
            BySignature[logicalFile.Signature.ToString()] = logicalFile;
            return Task.CompletedTask;
        }

        public Task UpdateCategoryAsync(LogicalFileId id, Arboryn.Domain.Enums.MediaCategory category, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<int> SetCategoryByInstanceAsync(FileInstanceId instanceId, Arboryn.Domain.Enums.MediaCategory category, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task BackfillUnattachedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteOrphansAsync(CancellationToken cancellationToken)
        {
            OrphansDeletedCount++;
            return Task.CompletedTask;
        }

        public Task<CatalogMetrics> GetMetricsAsync(VolumeId volumeId, CancellationToken cancellationToken)
            => throw new NotImplementedException("Pas utilisé par ce test.");

        public Task<IReadOnlyList<LogicalFileSummary>> GetSummariesAsync(CatalogFilter filter, CancellationToken cancellationToken)
            => throw new NotImplementedException("Pas utilisé par ce test.");

        public Task<CatalogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken)
            => throw new NotImplementedException("Pas utilisé par ce test.");
    }

    private sealed class FakeLinker : IFileInstanceLinker
    {
        public Dictionary<string, LogicalFileId> Attachments { get; } = new();

        public Task SetLogicalFileAsync(FileInstanceId id, LogicalFileId logicalFileId, CancellationToken cancellationToken)
        {
            Attachments[id.Value] = logicalFileId;
            return Task.CompletedTask;
        }
    }
}
