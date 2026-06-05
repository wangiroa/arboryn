using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.UseCases;

/// <summary>Vide le catalogue d'un volume (toutes ses FileInstances).</summary>
public sealed class ClearCatalogHandler
{
    private readonly IFileInstanceRepository _repository;

    public ClearCatalogHandler(IFileInstanceRepository repository)
        => _repository = repository;

    public Task ExecuteAsync(VolumeId volumeId, CancellationToken cancellationToken = default)
        => _repository.ClearVolumeAsync(volumeId, cancellationToken);
}
