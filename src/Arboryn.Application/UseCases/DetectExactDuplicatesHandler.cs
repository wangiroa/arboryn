using Arboryn.Application.Abstractions;
using Arboryn.Domain.Entities;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Détection des doublons exacts (Inc 1) : regroupe les FileInstances d'un volume
/// partageant le même nom canonique et la même taille.
/// </summary>
public sealed class DetectExactDuplicatesHandler
{
    private readonly IFileInstanceRepository _repository;

    public DetectExactDuplicatesHandler(IFileInstanceRepository repository)
        => _repository = repository;

    public async Task<IReadOnlyList<DuplicateGroup>> ExecuteAsync(
        VolumeId volumeId, CancellationToken cancellationToken = default)
    {
        var candidates = await _repository.GetDuplicateCandidatesAsync(volumeId, cancellationToken).ConfigureAwait(false);
        return Group(candidates);
    }

    /// <summary>
    /// Variante destinée à l'affichage : conserve les <see cref="FileInstanceRecord"/>
    /// membres (chemins, tailles). <paramref name="underRoot"/> limite éventuellement la
    /// détection au sous-arbre scanné ; <c>null</c> = tout le catalogue du volume.
    /// </summary>
    public async Task<IReadOnlyList<DuplicateGroupView>> ExecuteDetailedAsync(
        VolumeId volumeId, FilePath? underRoot = null, CancellationToken cancellationToken = default)
    {
        var candidates = await _repository.GetDuplicateCandidatesAsync(volumeId, underRoot, cancellationToken).ConfigureAwait(false);
        return GroupDetailed(candidates);
    }

    /// <summary>
    /// Regroupe par (nom canonique, taille) en ne conservant que les groupes de
    /// taille &gt; 1. Méthode pure, testable indépendamment du dépôt.
    /// </summary>
    public static IReadOnlyList<DuplicateGroup> Group(IReadOnlyList<FileInstanceRecord> candidates) =>
        GroupDetailed(candidates)
            .Select(view => new DuplicateGroup(
                DuplicateGroupId.New(),
                DuplicateGroupKind.ExactName,
                Confidence: 1.0,
                Members: view.Members.Select(m => m.Id).ToList()))
            .ToList();

    /// <summary>Regroupement détaillé (membres complets). Pur et testable.</summary>
    public static IReadOnlyList<DuplicateGroupView> GroupDetailed(IReadOnlyList<FileInstanceRecord> candidates) =>
        candidates
            .GroupBy(c => (Canonical: c.CanonicalName.Value, c.Size))
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateGroupView(DuplicateGroupKind.ExactName, g.ToList()))
            .ToList();
}

/// <summary>
/// Groupe de doublons enrichi des instances membres, pour l'affichage UI.
/// <see cref="Kind"/> distingue exact / flou / confirmé par hash. Les membres
/// peuvent avoir des tailles et noms différents (cas flou).
/// </summary>
public sealed record DuplicateGroupView(
    DuplicateGroupKind Kind,
    IReadOnlyList<FileInstanceRecord> Members);
