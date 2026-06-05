using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Réattribue le LogicalFile d'une FileInstance — utilisé par la promotion par hash
/// (Inc 3) qui consolide les variantes confirmées identiques sous un même LogicalFile.
/// </summary>
public interface IFileInstanceLinker
{
    Task SetLogicalFileAsync(FileInstanceId id, LogicalFileId logicalFileId, CancellationToken cancellationToken);
}
