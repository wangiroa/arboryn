using Arboryn.Application.Abstractions;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Matching;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Promotion acoustique (Inc 5) : consolide chaque groupe d'enregistrements du même
/// morceau sous un même <see cref="LogicalFile"/> à signature <c>chromaprint</c>
/// (empreinte du représentant), en y rattachant toutes les FileInstances. Les LogicalFiles
/// devenus orphelins (par ex. les LFs <c>name_size</c> des copies ré-encodées) sont nettoyés.
/// </summary>
public sealed class PromoteAudioHandler
{
    private readonly IAudioFingerprintStore _store;
    private readonly ILogicalFileRepository _logicalFiles;
    private readonly IFileInstanceLinker _linker;

    public PromoteAudioHandler(
        IAudioFingerprintStore store,
        ILogicalFileRepository logicalFiles,
        IFileInstanceLinker linker)
    {
        _store = store;
        _logicalFiles = logicalFiles;
        _linker = linker;
    }

    /// <summary>
    /// Détecte les groupes acoustiques d'un volume et les promeut. Renvoie le nombre de
    /// groupes consolidés.
    /// </summary>
    public async Task<int> ExecuteAsync(
        VolumeId volumeId,
        FilePath? underRoot = null,
        double minSimilarity = ChromaprintMatcher.DefaultMinSimilarity,
        CancellationToken cancellationToken = default)
    {
        var fingerprinted = await _store.GetFingerprintedAsync(volumeId, underRoot, cancellationToken).ConfigureAwait(false);
        var groups = DetectAudioDuplicatesHandler.GroupCore(fingerprinted, minSimilarity);
        if (groups.Count == 0)
        {
            return 0;
        }

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var signature = ContentSignature.FromChromaprint(group.Representative);
            var existing = await _logicalFiles.FindBySignatureAsync(signature, cancellationToken).ConfigureAwait(false);

            LogicalFileId logicalFileId;
            if (existing is not null)
            {
                logicalFileId = existing.Id;
            }
            else
            {
                var now = DateTime.UtcNow;
                var logicalFile = new LogicalFile(LogicalFileId.New(), MediaCategory.Audiobook, signature, now, now);
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
