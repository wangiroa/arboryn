using Arboryn.Application.Abstractions;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Supprime des FileInstances vers la corbeille, en journalisant chaque opération
/// sous un même <see cref="BatchId"/> pour permettre l'annulation. Le chemin du
/// fichier dans la corbeille est conservé (new_path) pour la restauration.
/// </summary>
public sealed class DeleteFilesHandler
{
    private readonly IRecycleBin _recycleBin;
    private readonly IOperationJournal _journal;
    private readonly IFileInstanceRepository _repository;
    private readonly ILogger<DeleteFilesHandler> _logger;

    public DeleteFilesHandler(
        IRecycleBin recycleBin,
        IOperationJournal journal,
        IFileInstanceRepository repository,
        ILogger<DeleteFilesHandler> logger)
    {
        _recycleBin = recycleBin;
        _journal = journal;
        _repository = repository;
        _logger = logger;
    }

    public async Task<DeleteResult> ExecuteAsync(
        IReadOnlyList<FileToDelete> files, CancellationToken cancellationToken = default)
    {
        var batchId = BatchId.New();
        var deleted = 0;
        var failed = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var recycledPath = await _recycleBin.SendToRecycleBinAsync(file.Path, cancellationToken).ConfigureAwait(false);
                await _repository.MarkDeletedAsync(file.Id, cancellationToken).ConfigureAwait(false);

                var now = DateTime.UtcNow;
                await _journal.AppendAsync(new Operation(
                    OperationId.New(),
                    batchId,
                    OperationKind.Delete,
                    file.Id,
                    OldPath: file.Path,
                    NewPath: recycledPath,
                    OperationStatus.Completed,
                    CreatedAt: now,
                    ExecutedAt: now), cancellationToken).ConfigureAwait(false);

                deleted++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec de la suppression de {Path}", file.Path);

                await _journal.AppendAsync(new Operation(
                    OperationId.New(),
                    batchId,
                    OperationKind.Delete,
                    file.Id,
                    OldPath: file.Path,
                    NewPath: null,
                    OperationStatus.Failed,
                    CreatedAt: DateTime.UtcNow), cancellationToken).ConfigureAwait(false);

                failed++;
            }
        }

        return new DeleteResult(batchId, deleted, failed);
    }
}

/// <summary>Fichier à supprimer : son identité de catalogue et son chemin absolu.</summary>
public sealed record FileToDelete(FileInstanceId Id, FilePath Path);

/// <summary>Résultat d'une suppression en lot.</summary>
public sealed record DeleteResult(BatchId BatchId, int Deleted, int Failed);
