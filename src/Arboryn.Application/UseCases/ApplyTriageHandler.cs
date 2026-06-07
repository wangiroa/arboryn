using System.IO;
using Arboryn.Application.Abstractions;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Triage;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Arboryn.Application.UseCases;

/// <summary>
/// Décision de triage validée par l'utilisateur pour un document : les trois champs retenus
/// (source / objet / date normalisée <c>yyyyMM</c>), la sous-catégorie de placement, et les
/// valeurs initialement pré-remplies (pour alimenter l'apprentissage des corrections).
/// </summary>
public sealed record TriageDecision(
    FileInstanceId InstanceId,
    FilePath Path,
    string Source,
    string Object,
    string Date,
    string Subcategory,
    string Snippet,
    string? OriginalSource = null,
    string? OriginalObject = null);

/// <summary>
/// Applique un lot de décisions de triage : enregistre les champs comme métadonnées (source
/// <c>triage</c>), place chaque document sous son chemin canonique de document officiel
/// (<c>Documents officiels/{sous-catégorie}/[source] - objet - date.ext</c>) via le moteur
/// d'uniformisation (donc annulable), et journalise les corrections utilisateur pour
/// l'apprentissage. Réutilise <see cref="ExecuteUniformizationHandler"/> → annulable par
/// « Annuler la dernière uniformisation ».
/// </summary>
public sealed class ApplyTriageHandler
{
    private readonly IFileMetadataRepository _metadata;
    private readonly ITaxonomyRepository _taxonomies;
    private readonly ITriageRepository _triage;
    private readonly ILogicalFileRepository _logicalFiles;
    private readonly CanonicalPathResolver _resolver;
    private readonly ExecuteUniformizationHandler _executor;
    private readonly IFileMover _mover;
    private readonly ILogger<ApplyTriageHandler> _logger;

    public ApplyTriageHandler(
        IFileMetadataRepository metadata,
        ITaxonomyRepository taxonomies,
        ITriageRepository triage,
        ILogicalFileRepository logicalFiles,
        CanonicalPathResolver resolver,
        ExecuteUniformizationHandler executor,
        IFileMover mover,
        ILogger<ApplyTriageHandler> logger)
    {
        _metadata = metadata;
        _taxonomies = taxonomies;
        _triage = triage;
        _logicalFiles = logicalFiles;
        _resolver = resolver;
        _executor = executor;
        _mover = mover;
        _logger = logger;
    }

    public async Task<TriageApplyResult> ExecuteAsync(
        IReadOnlyList<TriageDecision> decisions, FilePath libraryRoot, CancellationToken cancellationToken = default)
    {
        var taxonomy = await _taxonomies.GetAsync(MediaCategory.OfficialDocument, cancellationToken).ConfigureAwait(false);
        if (taxonomy is null)
        {
            throw new InvalidOperationException("Aucune taxonomie pour la catégorie « Documents officiels ».");
        }

        var targets = new List<(FileInstanceRecord Instance, string TargetRelative)>();
        var skipped = 0;

        foreach (var decision in decisions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fields = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["source"] = decision.Source,
                ["object"] = decision.Object,
                ["date"] = decision.Date,
                ["subcategory"] = decision.Subcategory,
                ["ext"] = decision.Path.Extension.TrimStart('.'),
            };

            var placement = _resolver.Resolve(taxonomy, fields);
            if (placement is null)
            {
                skipped++;
                continue;
            }

            await SaveFieldsAsync(decision, cancellationToken).ConfigureAwait(false);
            await RecordCorrectionsAsync(decision, cancellationToken).ConfigureAwait(false);

            // Affine la catégorie du LogicalFile : le document devient officiellement un
            // « Document officiel », pour que l'uniformisation (Inc 6) le traite désormais
            // avec la bonne taxonomie au lieu de « PDF divers » (déduit de l'extension).
            await _logicalFiles
                .SetCategoryByInstanceAsync(decision.InstanceId, MediaCategory.OfficialDocument, cancellationToken)
                .ConfigureAwait(false);

            targets.Add((ToRecord(decision), placement.RelativePath));
        }

        var (operations, alreadyCanonical) =
            PlanUniformizationHandler.BuildOperations(targets, libraryRoot.Value, _mover.Exists);

        var plan = new UniformizationPlan(operations, alreadyCanonical, skipped);
        var result = await _executor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Triage appliqué : {Applied} placé(s), {Already} déjà conforme(s), {Skipped} ignoré(s), {Failed} échec(s).",
            result.Moved, alreadyCanonical, skipped, result.Failed);

        return new TriageApplyResult(result.BatchId, result.Moved, alreadyCanonical, skipped, result.Failed);
    }

    private async Task SaveFieldsAsync(TriageDecision decision, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var entries = new[]
        {
            new MetadataEntry(decision.InstanceId, "source", decision.Source, MetadataSources.Triage, 1.0, now),
            new MetadataEntry(decision.InstanceId, "object", decision.Object, MetadataSources.Triage, 1.0, now),
            new MetadataEntry(decision.InstanceId, "date", decision.Date, MetadataSources.Triage, 1.0, now),
            new MetadataEntry(decision.InstanceId, "subcategory", decision.Subcategory, MetadataSources.Triage, 1.0, now),
        };

        foreach (var entry in entries)
        {
            await _metadata.UpsertAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RecordCorrectionsAsync(TriageDecision decision, CancellationToken cancellationToken)
    {
        if (IsCorrection(decision.OriginalSource, decision.Source))
        {
            await _triage.AddCorrectionAsync(decision.InstanceId, new TriageCorrection(
                TriagePatternKind.Source, decision.Snippet, decision.OriginalSource, decision.Source),
                cancellationToken).ConfigureAwait(false);
        }

        if (IsCorrection(decision.OriginalObject, decision.Object))
        {
            await _triage.AddCorrectionAsync(decision.InstanceId, new TriageCorrection(
                TriagePatternKind.Object, decision.Snippet, decision.OriginalObject, decision.Object),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsCorrection(string? original, string final)
        => !string.IsNullOrWhiteSpace(final)
           && !string.Equals(original?.Trim(), final.Trim(), StringComparison.OrdinalIgnoreCase);

    private static FileInstanceRecord ToRecord(TriageDecision decision) => new(
        decision.InstanceId,
        VolumeId.Default,
        decision.Path,
        CanonicalName.From(Path.GetFileName(decision.Path.Value)),
        Size: 0,
        ModifiedAt: default);
}

/// <summary>Résultat de l'application d'un lot de triage.</summary>
public sealed record TriageApplyResult(
    BatchId BatchId, int Applied, int AlreadyCanonical, int Skipped, int Failed);
