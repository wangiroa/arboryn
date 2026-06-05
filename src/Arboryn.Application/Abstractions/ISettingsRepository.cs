namespace Arboryn.Application.Abstractions;

/// <summary>Stockage clé-valeur persistant (table <c>settings</c>).</summary>
public interface ISettingsRepository
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(string key, string value, CancellationToken cancellationToken);
}
