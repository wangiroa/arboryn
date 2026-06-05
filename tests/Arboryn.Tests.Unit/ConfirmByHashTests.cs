using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class ConfirmByHashTests
{
    [Fact]
    public async Task Execute_GroupsTargetsByIdenticalHash_AndPersists()
    {
        var hashA = new string('a', 64);
        var hashC = new string('c', 64);
        var hasher = new FakeHasher(new Dictionary<string, string>
        {
            [@"C:\1\file.pdf"] = hashA,
            [@"C:\2\file.pdf"] = hashA,
            [@"C:\3\file.pdf"] = hashC,
        });
        var store = new FakeHashStore();

        var handler = new ConfirmByHashHandler(hasher, store);
        var targets = new[]
        {
            new HashTarget(FileInstanceId.New(), FilePath.From(@"C:\1\file.pdf")),
            new HashTarget(FileInstanceId.New(), FilePath.From(@"C:\2\file.pdf")),
            new HashTarget(FileInstanceId.New(), FilePath.From(@"C:\3\file.pdf")),
        };

        var groups = await handler.ExecuteAsync(targets);

        groups.Should().HaveCount(2);
        groups.Single(g => g.Members.Count == 2).Hash.Value.Should().Be(hashA);

        // Les trois empreintes ont été calculées puis persistées.
        hasher.ComputeCount.Should().Be(3);
        store.Saved.Should().HaveCount(3);
    }

    [Fact]
    public async Task Execute_UsesCachedHash_WithoutRecomputing()
    {
        var hasher = new FakeHasher(new Dictionary<string, string>());
        var store = new FakeHashStore();
        var id = FileInstanceId.New();
        store.Saved[id.Value] = Sha256.FromHex(new string('b', 64)); // déjà en cache

        var handler = new ConfirmByHashHandler(hasher, store);
        var groups = await handler.ExecuteAsync(new[] { new HashTarget(id, FilePath.From(@"C:\x\file.pdf")) });

        groups.Should().ContainSingle();
        hasher.ComputeCount.Should().Be(0, "le hash en cache doit être réutilisé");
    }

    private sealed class FakeHasher : IFileHasher
    {
        private readonly IReadOnlyDictionary<string, string> _hashes;

        public FakeHasher(IReadOnlyDictionary<string, string> hashes) => _hashes = hashes;

        public int ComputeCount { get; private set; }

        public Task<Sha256> ComputeAsync(FilePath path, CancellationToken cancellationToken)
        {
            ComputeCount++;
            return Task.FromResult(Sha256.FromHex(_hashes[path.Value]));
        }
    }

    private sealed class FakeHashStore : IFileHashStore
    {
        public Dictionary<string, Sha256> Saved { get; } = new();

        public Task<Sha256?> GetAsync(FileInstanceId id, CancellationToken cancellationToken)
            => Task.FromResult(Saved.TryGetValue(id.Value, out var h) ? h : (Sha256?)null);

        public Task SetAsync(FileInstanceId id, Sha256 hash, CancellationToken cancellationToken)
        {
            Saved[id.Value] = hash;
            return Task.CompletedTask;
        }
    }
}
