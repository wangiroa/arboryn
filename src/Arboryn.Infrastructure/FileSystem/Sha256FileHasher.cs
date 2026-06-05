using System.Security.Cryptography;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>Calcule le SHA-256 d'un fichier en streaming (lecture séquentielle asynchrone).</summary>
public sealed class Sha256FileHasher : IFileHasher
{
    public async Task<Sha256> ComputeAsync(FilePath path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path.Value,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Sha256.FromBytes(hash);
    }
}
