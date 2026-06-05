using Arboryn.Application.Abstractions;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Promotion par hash : à partir des groupes <see cref="HashGroup"/> renvoyés par
/// <see cref="ConfirmByHashHandler"/>, consolide chaque groupe (≥ 1 membre) sous un
/// <see cref="LogicalFile"/> à signature <c>sha256</c>, en y rattachant toutes les
/// FileInstances concernées. Les anciens LogicalFiles devenus orphelins (typiquement
/// les LFs <c>name_size</c> des variantes confirmées identiques) sont nettoyés.
/// </summary>
public sealed class PromoteByHashHandler
{
    private readonly ILogicalFileRepository _logicalFiles;
    private readonly IFileInstanceLinker _linker;

    public PromoteByHashHandler(ILogicalFileRepository logicalFiles, IFileInstanceLinker linker)
    {
        _logicalFiles = logicalFiles;
        _linker = linker;
    }

    public async Task ExecuteAsync(
        IReadOnlyList<HashGroup> hashGroups, CancellationToken cancellationToken = default)
    {
        if (hashGroups.Count == 0)
        {
            return;
        }

        foreach (var hashGroup in hashGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var signature = ContentSignature.FromSha256(hashGroup.Hash);
            var existing = await _logicalFiles.FindBySignatureAsync(signature, cancellationToken).ConfigureAwait(false);

            LogicalFileId logicalFileId;
            if (existing is not null)
            {
                logicalFileId = existing.Id;
            }
            else
            {
                var now = DateTime.UtcNow;
                var logicalFile = new LogicalFile(LogicalFileId.New(), MediaCategory.Unknown, signature, now, now);
                await _logicalFiles.UpsertAsync(logicalFile, cancellationToken).ConfigureAwait(false);
                logicalFileId = logicalFile.Id;
            }

            foreach (var target in hashGroup.Members)
            {
                await _linker.SetLogicalFileAsync(target.Id, logicalFileId, cancellationToken).ConfigureAwait(false);
            }
        }

        // Les LFs name_size désormais orphelins (toutes leurs instances ont migré vers
        // un LF sha256) sont nettoyés ici, pour que l'inventaire reflète la réalité.
        await _logicalFiles.DeleteOrphansAsync(cancellationToken).ConfigureAwait(false);
    }
}
