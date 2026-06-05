using System.Runtime.Versioning;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.FileSystem;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

/// <summary>
/// Vérifie l'adapter corbeille réel (COM IFileOperation) de bout en bout :
/// envoi à la corbeille puis restauration par déplacement inverse.
/// </summary>
[SupportedOSPlatform("windows")]
public class RecycleBinTests
{
    [Fact]
    public async Task SendToRecycleBin_ThenRestore_RoundTripsRealFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"Arboryn-bin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "to-recycle.txt");
        File.WriteAllText(file, "data");

        var bin = new RecycleBin(NullLogger<RecycleBin>.Instance);

        try
        {
            var recycled = await bin.SendToRecycleBinAsync(FilePath.From(file), CancellationToken.None);

            File.Exists(file).Should().BeFalse("le fichier doit avoir été envoyé à la corbeille");
            recycled.Should().NotBeNull("le chemin dans la corbeille doit être capturé pour l'undo");

            var restored = await bin.RestoreAsync(recycled!.Value, FilePath.From(file), CancellationToken.None);

            restored.Should().BeTrue();
            File.Exists(file).Should().BeTrue("le fichier doit avoir été restauré");
            File.ReadAllText(file).Should().Be("data");
        }
        finally
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (IOException)
            {
                // Nettoyage best-effort.
            }
        }
    }
}
