using Arboryn.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Révision des candidats d'enrichissement (suivi Inc 8) : liste les champs proposés sous le
/// seuil d'auto-application et applique la décision de l'utilisateur. Accepter écrit la
/// métadonnée dans <c>file_metadata</c> (source <c>online_&lt;provider&gt;</c>) ; rejeter marque
/// simplement le candidat. Aucun appel réseau ici — tout est déjà en base.
/// </summary>
public sealed class ReviewEnrichmentCandidatesHandler
{
    private readonly IEnrichmentCandidateRepository _candidates;
    private readonly IFileMetadataRepository _metadata;
    private readonly ILogger<ReviewEnrichmentCandidatesHandler> _logger;

    public ReviewEnrichmentCandidatesHandler(
        IEnrichmentCandidateRepository candidates,
        IFileMetadataRepository metadata,
        ILogger<ReviewEnrichmentCandidatesHandler> logger)
    {
        _candidates = candidates;
        _metadata = metadata;
        _logger = logger;
    }

    /// <summary>Candidats en attente de décision, prêts pour l'affichage.</summary>
    public Task<IReadOnlyList<PendingEnrichmentCandidate>> ListPendingAsync(CancellationToken cancellationToken = default)
        => _candidates.GetPendingAsync(cancellationToken);

    /// <summary>Nombre de candidats en attente (badge / résumé).</summary>
    public Task<int> CountPendingAsync(CancellationToken cancellationToken = default)
        => _candidates.CountPendingAsync(cancellationToken);

    /// <summary>
    /// Accepte un candidat : écrit la valeur dans <c>file_metadata</c> et marque le candidat
    /// accepté. L'utilisateur validant explicitement, la métadonnée est écrite avec une confiance
    /// maximale (sa provenance reste tracée par la source <c>online_&lt;provider&gt;</c>) afin de
    /// primer dans la fusion. Renvoie <c>false</c> si le candidat n'existe plus.
    /// </summary>
    public async Task<bool> AcceptAsync(string candidateId, CancellationToken cancellationToken = default)
    {
        var candidate = await _candidates.GetAsync(candidateId, cancellationToken).ConfigureAwait(false);
        if (candidate is null)
        {
            return false;
        }

        await _metadata.UpsertAsync(new MetadataEntry(
            candidate.InstanceId, candidate.Key, candidate.Value,
            MetadataSources.Online(candidate.Provider), Confidence: 1.0, DateTime.UtcNow),
            cancellationToken).ConfigureAwait(false);

        await _candidates.SetStatusAsync(candidateId, EnrichmentCandidateStatus.Accepted, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Candidat d'enrichissement accepté : {Key}={Value} ({Provider}) pour {Instance}.",
            candidate.Key, candidate.Value, candidate.Provider, candidate.InstanceId.Value);
        return true;
    }

    /// <summary>Rejette un candidat (marqué rejeté, non écrit). Renvoie <c>false</c> s'il n'existe plus.</summary>
    public async Task<bool> RejectAsync(string candidateId, CancellationToken cancellationToken = default)
    {
        var candidate = await _candidates.GetAsync(candidateId, cancellationToken).ConfigureAwait(false);
        if (candidate is null)
        {
            return false;
        }

        await _candidates.SetStatusAsync(candidateId, EnrichmentCandidateStatus.Rejected, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Candidat d'enrichissement rejeté : {Key}={Value} ({Provider}) pour {Instance}.",
            candidate.Key, candidate.Value, candidate.Provider, candidate.InstanceId.Value);
        return true;
    }
}
