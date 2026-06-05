using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Domain.Entities;

/// <summary>
/// Entrée du journal d'opérations. Sert à l'exécution puis à l'annulation
/// (rejouage inverse). En Inc 1, seul <see cref="OperationKind.Delete"/> est
/// produit ; <see cref="OldPath"/> conserve l'emplacement d'origine pour permettre
/// la restauration depuis la corbeille.
/// </summary>
public sealed record Operation(
    OperationId Id,
    BatchId BatchId,
    OperationKind Kind,
    FileInstanceId FileInstanceId,
    FilePath? OldPath,
    FilePath? NewPath,
    OperationStatus Status,
    DateTime CreatedAt,
    DateTime? ExecutedAt = null,
    DateTime? UndoneAt = null);
