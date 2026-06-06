using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Calcule l'empreinte acoustique (Chromaprint) d'un fichier audio. Renvoie <c>null</c>
/// si le fichier n'est pas décodable ou si l'outil externe (fpcalc) est indisponible.
/// </summary>
public interface IAudioFingerprinter
{
    Task<AudioFingerprint?> ComputeAsync(FilePath path, CancellationToken cancellationToken);
}
