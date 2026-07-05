using Arboryn.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Arboryn.Application;

/// <summary>Enregistrement DI des use cases de la couche Application.</summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddArborynApplication(this IServiceCollection services)
    {
        services.AddTransient<LogicalFileResolver>();
        services.AddTransient<ScanDirectoryHandler>();
        services.AddTransient<RescanVolumeHandler>();
        services.AddTransient<DetectExactDuplicatesHandler>();
        services.AddTransient<DetectFuzzyDuplicatesHandler>();
        services.AddTransient<ClearCatalogHandler>();
        services.AddTransient<DeleteFilesHandler>();
        services.AddTransient<UndoLastBatchHandler>();
        services.AddTransient<ConfirmByHashHandler>();
        services.AddTransient<PromoteByHashHandler>();
        services.AddTransient<ExtractMetadataHandler>();
        services.AddTransient<ComputePerceptualHashesHandler>();
        services.AddTransient<DetectPerceptualDuplicatesHandler>();
        services.AddTransient<PromotePerceptualHandler>();
        services.AddTransient<ComputeAudioFingerprintsHandler>();
        services.AddTransient<DetectAudioDuplicatesHandler>();
        services.AddTransient<PromoteAudioHandler>();
        services.AddTransient<CanonicalPathResolver>();
        services.AddTransient<UpgradeDefaultTaxonomiesHandler>();
        services.AddTransient<PlanUniformizationHandler>();
        services.AddTransient<ExecuteUniformizationHandler>();
        services.AddTransient<UndoUniformizationHandler>();
        services.AddTransient<WriteBackMetadataHandler>();
        services.AddTransient<UndoWriteBackMetadataHandler>();
        services.AddTransient<PrepareTriageHandler>();
        services.AddTransient<ApplyTriageHandler>();
        services.AddTransient<LearnTriagePatternsHandler>();
        services.AddTransient<EnrichMetadataHandler>();
        services.AddTransient<EnrichDirectoryHandler>();
        services.AddTransient<ReviewEnrichmentCandidatesHandler>();
        services.AddTransient<EnrollVolumeHandler>();
        // Réplication multi-support (Inc 10) — assemblage du catalogue + calcul du plan de placement.
        services.AddTransient<Replication.PlacementPlanCalculator>();
        services.AddTransient<Replication.BuildReplicationCatalogHandler>();
        services.AddTransient<Replication.BuildReplicationPlanHandler>();
        services.AddTransient<Replication.ReplicationOperationExecutor>();
        services.AddTransient<Replication.ExecuteReplicationPlanHandler>();
        services.AddTransient<Replication.ResumePendingReplicationHandler>();
        services.AddTransient<Replication.UndoReplicationBatchHandler>();
        return services;
    }
}
