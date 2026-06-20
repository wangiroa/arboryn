using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.Abstractions;

/// <summary>
/// Persistance des candidats d'enrichissement (suivi Inc 8) : les champs proposés par les
/// providers en ligne sous le seuil d'auto-application, en attente d'une décision utilisateur
/// (accepter / rejeter) via l'UI de révision.
/// </summary>
public interface IEnrichmentCandidateRepository
{
    /// <summary>
    /// Insère ou met à jour un candidat (unicité : instance + provider + clé). Si la valeur
    /// proposée diffère de la précédente, le statut repasse à <c>pending</c> ; sinon le statut
    /// existant est conservé (un candidat rejeté n'est pas ressuscité par un re-enrichissement
    /// proposant la même valeur).
    /// </summary>
    Task UpsertAsync(EnrichmentCandidateRecord candidate, CancellationToken cancellationToken);

    /// <summary>Candidats en attente, joints à leur fichier pour l'affichage, triés par confiance.</summary>
    Task<IReadOnlyList<PendingEnrichmentCandidate>> GetPendingAsync(CancellationToken cancellationToken);

    /// <summary>Renvoie un candidat par son id, ou <c>null</c> s'il n'existe pas.</summary>
    Task<EnrichmentCandidateRecord?> GetAsync(string candidateId, CancellationToken cancellationToken);

    /// <summary>Met à jour le statut d'un candidat (et horodate la décision si elle est définitive).</summary>
    Task SetStatusAsync(string candidateId, EnrichmentCandidateStatus status, CancellationToken cancellationToken);

    /// <summary>Nombre de candidats en attente de décision.</summary>
    Task<int> CountPendingAsync(CancellationToken cancellationToken);
}

/// <summary>Statut de décision d'un candidat d'enrichissement.</summary>
public enum EnrichmentCandidateStatus
{
    Pending,
    Accepted,
    Rejected,
}

/// <summary>Un candidat d'enrichissement persisté.</summary>
public sealed record EnrichmentCandidateRecord(
    string Id,
    FileInstanceId InstanceId,
    string Provider,
    string Key,
    string Value,
    double Confidence,
    EnrichmentCandidateStatus Status);

/// <summary>Candidat en attente, enrichi du chemin du fichier pour l'affichage dans l'UI de révision.</summary>
public sealed record PendingEnrichmentCandidate(
    string Id,
    FileInstanceId InstanceId,
    string Path,
    string Provider,
    string Key,
    string Value,
    double Confidence);
