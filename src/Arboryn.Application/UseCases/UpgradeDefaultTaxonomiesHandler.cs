using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Taxonomy;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Met à niveau les taxonomies stockées qui ne sont que d'anciens défauts livrés (Inc 6+).
///
/// Une taxonomie enregistrée en base masque le défaut du code (cf. <see cref="ITaxonomyRepository.GetAsync"/>),
/// si bien qu'une amélioration d'un template par défaut n'atteint jamais un utilisateur ayant
/// déjà enregistré une taxonomie — même non modifiée. Ce handler, lancé au démarrage, supprime
/// les lignes stockées qui correspondent à un défaut livré (courant ou ancien, cf.
/// <see cref="DefaultTaxonomies.IsShippedDefault"/>) : la catégorie repasse alors au défaut du
/// code. Les véritables personnalisations (qui ne correspondent à aucun défaut livré) sont
/// préservées. Idempotent.
/// </summary>
public sealed class UpgradeDefaultTaxonomiesHandler
{
    private readonly ITaxonomyRepository _taxonomies;
    private readonly ILogger<UpgradeDefaultTaxonomiesHandler> _logger;

    public UpgradeDefaultTaxonomiesHandler(
        ITaxonomyRepository taxonomies, ILogger<UpgradeDefaultTaxonomiesHandler> logger)
    {
        _taxonomies = taxonomies;
        _logger = logger;
    }

    /// <summary>Renvoie le nombre de catégories repassées au défaut du code.</summary>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var upgraded = 0;
        foreach (var category in Enum.GetValues<MediaCategory>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stored = await _taxonomies.GetStoredAsync(category, cancellationToken).ConfigureAwait(false);
            if (stored is null || !DefaultTaxonomies.IsShippedDefault(stored))
            {
                continue;
            }

            await _taxonomies.DeleteAsync(category, cancellationToken).ConfigureAwait(false);
            upgraded++;
            _logger.LogInformation(
                "Taxonomie {Category} : ancien défaut stocké supprimé, retour au défaut courant du code.",
                category);
        }

        return upgraded;
    }
}
