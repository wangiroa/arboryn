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
    DateTime? UndoneAt = null,
    /// <summary>Métadonnées d'origine (JSON) pour annuler un write-back (<see cref="OperationKind.MetadataWriteback"/>).</summary>
    string? OldMetadataJson = null,
    /// <summary>Volume source (réplication Inc 10) — volume agi pour rename/move/delete, source pour copy.</summary>
    VolumeId? SourceVolumeId = null,
    /// <summary>Volume cible (réplication Inc 10) — destination d'une copy inter-volume.</summary>
    VolumeId? TargetVolumeId = null);
