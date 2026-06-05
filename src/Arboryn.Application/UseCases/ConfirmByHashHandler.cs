using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Confirme un groupe (souvent flou) par hachage SHA-256 : calcule l'empreinte de
/// chaque fichier et les regroupe par contenu. Les groupes de ≥ 2 membres partageant
/// la même empreinte sont des copies byte-à-byte ; les empreintes uniques sont de
/// vraies variantes.
/// </summary>
public sealed class ConfirmByHashHandler
{
    private readonly IFileHasher _hasher;
    private readonly IFileHashStore _hashStore;

    public ConfirmByHashHandler(IFileHasher hasher, IFileHashStore hashStore)
    {
        _hasher = hasher;
        _hashStore = hashStore;
    }

    public async Task<IReadOnlyList<HashGroup>> ExecuteAsync(
        IReadOnlyList<HashTarget> targets, CancellationToken cancellationToken = default)
    {
        var hashed = new List<(HashTarget Target, Sha256 Hash)>(targets.Count);
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Réutilise l'empreinte mémorisée si présente, sinon calcule puis persiste.
            var hash = await _hashStore.GetAsync(target.Id, cancellationToken).ConfigureAwait(false);
            if (hash is null)
            {
                hash = await _hasher.ComputeAsync(target.Path, cancellationToken).ConfigureAwait(false);
                await _hashStore.SetAsync(target.Id, hash.Value, cancellationToken).ConfigureAwait(false);
            }

            hashed.Add((target, hash.Value));
        }

        return hashed
            .GroupBy(x => x.Hash.Value, StringComparer.Ordinal)
            .Select(g => new HashGroup(g.First().Hash, g.Select(x => x.Target).ToList()))
            .ToList();
    }
}

/// <summary>Fichier à hacher : identité de catalogue + chemin absolu.</summary>
public sealed record HashTarget(FileInstanceId Id, FilePath Path);

/// <summary>Ensemble de fichiers partageant la même empreinte SHA-256.</summary>
public sealed record HashGroup(Sha256 Hash, IReadOnlyList<HashTarget> Members);
