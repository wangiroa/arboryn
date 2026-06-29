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
        return services;
    }
}
