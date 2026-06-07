using Arboryn.Application.Abstractions;
using Arboryn.Domain.Triage;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Dérive de nouveaux patterns de triage à partir des corrections utilisateur non encore
/// exploitées (job d'apprentissage). Pensé pour tourner en arrière-plan ou à la demande après
/// un lot de triage : chaque correction exploitable devient un pattern littéral à priorité
/// élevée, qui pré-remplira automatiquement les documents suivants du même émetteur/type.
/// Idempotent : les corrections déjà dérivées et les patterns en doublon sont ignorés.
/// </summary>
public sealed class LearnTriagePatternsHandler
{
    private readonly ITriageRepository _triage;
    private readonly ILogger<LearnTriagePatternsHandler> _logger;

    public LearnTriagePatternsHandler(ITriageRepository triage, ILogger<LearnTriagePatternsHandler> logger)
    {
        _triage = triage;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var corrections = await _triage.GetUnderivedCorrectionsAsync(cancellationToken).ConfigureAwait(false);
        var learned = 0;

        foreach (var stored in corrections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pattern = TriagePatternLearner.Derive(stored.Correction);
            if (pattern is null)
            {
                continue;
            }

            // Évite les doublons : si un pattern identique existe déjà, on rattache simplement
            // la correction sans recréer de pattern.
            var exists = await _triage.PatternExistsAsync(pattern.Kind, pattern.Regex, cancellationToken).ConfigureAwait(false);
            if (exists)
            {
                continue;
            }

            var patternId = await _triage.AddPatternAsync(pattern, cancellationToken).ConfigureAwait(false);
            await _triage.MarkCorrectionDerivedAsync(stored.Id, patternId, cancellationToken).ConfigureAwait(false);
            learned++;
        }

        _logger.LogInformation("Apprentissage triage : {Count} nouveau(x) pattern(s) dérivé(s).", learned);
        return learned;
    }
}
