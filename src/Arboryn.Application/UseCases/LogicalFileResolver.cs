using Arboryn.Application.Abstractions;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Rattache une signature de contenu à un <see cref="LogicalFile"/> existant, ou en crée
/// un nouveau. Partagé par l'indexation initiale (<see cref="ScanDirectoryHandler"/>) et le
/// re-scan incrémental (<see cref="RescanVolumeHandler"/>). Le cache (clé = signature) évite
/// les allers-retours base pour les copies multiples rencontrées pendant un même scan.
/// </summary>
public sealed class LogicalFileResolver
{
    private readonly ILogicalFileRepository _logicalFileRepository;

    public LogicalFileResolver(ILogicalFileRepository logicalFileRepository)
        => _logicalFileRepository = logicalFileRepository;

    public async Task<LogicalFileId> ResolveAsync(
        ContentSignature signature,
        MediaCategory category,
        Dictionary<string, LogicalFileId> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(signature.Value, out var cached))
        {
            return cached;
        }

        var existing = await _logicalFileRepository
            .FindBySignatureAsync(signature, cancellationToken).ConfigureAwait(false);

        LogicalFileId id;
        if (existing is not null)
        {
            id = existing.Id;
        }
        else
        {
            // Catégorie préliminaire déduite de l'extension (affinée plus tard par le
            // contenu / le triage). Les instances partageant une signature partagent
            // l'extension, donc la catégorie est cohérente.
            var now = DateTime.UtcNow;
            var logicalFile = new LogicalFile(LogicalFileId.New(), category, signature, now, now);
            await _logicalFileRepository.UpsertAsync(logicalFile, cancellationToken).ConfigureAwait(false);
            id = logicalFile.Id;
        }

        cache[signature.Value] = id;
        return id;
    }
}
