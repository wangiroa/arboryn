using Arboryn.Application.Abstractions;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Promotion perceptuelle (Inc 5) : consolide chaque groupe d'images visuellement proches
/// sous un même <see cref="LogicalFile"/> à signature <c>phash</c> (empreinte du représentant),
/// en y rattachant toutes les FileInstances. Les LogicalFiles devenus orphelins (typiquement
/// les LFs <c>name_size</c> des copies recompressées) sont nettoyés.
/// </summary>
public sealed class PromotePerceptualHandler
{
    private readonly IPerceptualHashStore _store;
    private readonly ILogicalFileRepository _logicalFiles;
    private readonly IFileInstanceLinker _linker;

    public PromotePerceptualHandler(
        IPerceptualHashStore store,
        ILogicalFileRepository logicalFiles,
        IFileInstanceLinker linker)
    {
        _store = store;
        _logicalFiles = logicalFiles;
        _linker = linker;
    }

    /// <summary>
    /// Détecte les groupes perceptuels d'un volume et les promeut. Renvoie le nombre de
    /// groupes consolidés.
    /// </summary>
    public async Task<int> ExecuteAsync(
        VolumeId volumeId,
        FilePath? underRoot = null,
        int maxDistance = DetectPerceptualDuplicatesHandler.DefaultMaxDistance,
        CancellationToken cancellationToken = default)
    {
        var hashed = await _store.GetHashedAsync(volumeId, underRoot, cancellationToken).ConfigureAwait(false);
        var groups = DetectPerceptualDuplicatesHandler.GroupCore(hashed, maxDistance);
        if (groups.Count == 0)
        {
            return 0;
        }

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var signature = ContentSignature.FromPerceptualHash(group.Representative);
            var existing = await _logicalFiles.FindBySignatureAsync(signature, cancellationToken).ConfigureAwait(false);

            LogicalFileId logicalFileId;
            if (existing is not null)
            {
                logicalFileId = existing.Id;
            }
            else
            {
                var now = DateTime.UtcNow;
                var logicalFile = new LogicalFile(LogicalFileId.New(), MediaCategory.Photo, signature, now, now);
                await _logicalFiles.UpsertAsync(logicalFile, cancellationToken).ConfigureAwait(false);
                logicalFileId = logicalFile.Id;
            }

            foreach (var member in group.Members)
            {
                await _linker.SetLogicalFileAsync(member.Instance.Id, logicalFileId, cancellationToken).ConfigureAwait(false);
            }
        }

        await _logicalFiles.DeleteOrphansAsync(cancellationToken).ConfigureAwait(false);
        return groups.Count;
    }
}
