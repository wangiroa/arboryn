using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Implémentation <see cref="IFileMover"/> sur le système de fichiers : crée l'arborescence
/// cible puis déplace le fichier sans écraser une cible existante (la résolution de conflits
/// est faite en amont, à la planification).
/// </summary>
public sealed class FileSystemMover : IFileMover
{
    public bool Exists(FilePath path) => File.Exists(path.Value);

    public Task MoveAsync(FilePath source, FilePath target, CancellationToken cancellationToken)
        => Task.Run(
            () =>
            {
                var directory = Path.GetDirectoryName(target.Value);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.Move(source.Value, target.Value, overwrite: false);
            },
            cancellationToken);

    public Task CopyAsync(FilePath source, FilePath target, CancellationToken cancellationToken)
        => Task.Run(
            () =>
            {
                var directory = Path.GetDirectoryName(target.Value);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.Copy(source.Value, target.Value, overwrite: false);
            },
            cancellationToken);
}
