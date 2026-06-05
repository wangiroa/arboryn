using Arboryn.Application.Abstractions;
using Arboryn.Infrastructure.Database;
using Arboryn.Infrastructure.FileSystem;
using Arboryn.Infrastructure.Persistence;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arboryn.Infrastructure;

/// <summary>Enregistrement DI des adapters de la couche Infrastructure.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    // L'infrastructure cible Windows (corbeille via COM IFileOperation).
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddArborynInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(new DatabaseFactory(connectionString));
        services.AddSingleton(sp => new Migrator(
            connectionString, sp.GetRequiredService<ILogger<Migrator>>()));
        services.AddSingleton<IFileScanner, FileScanner>();
        // Une seule instance partagée pour les deux interfaces du dépôt.
        services.AddSingleton<SqliteFileInstanceRepository>();
        services.AddSingleton<IFileInstanceRepository>(sp => sp.GetRequiredService<SqliteFileInstanceRepository>());
        services.AddSingleton<IFileHashStore>(sp => sp.GetRequiredService<SqliteFileInstanceRepository>());
        services.AddSingleton<IFileInstanceLinker>(sp => sp.GetRequiredService<SqliteFileInstanceRepository>());
        services.AddSingleton<IPerceptualHashStore>(sp => sp.GetRequiredService<SqliteFileInstanceRepository>());
        services.AddSingleton<ILogicalFileRepository, SqliteLogicalFileRepository>();
        services.AddSingleton<IFileMetadataRepository, SqliteFileMetadataRepository>();
        services.AddSingleton<IOperationJournal, SqliteOperationJournal>();
        services.AddSingleton<IRecycleBin, RecycleBin>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        services.AddSingleton<IFileHasher, Sha256FileHasher>();

        // Lecteurs de métadonnées de contenu (Inc 4) — appliqués par catégorie.
        services.AddSingleton<IContentMetadataReader, TagLibAudioMetadataReader>();
        services.AddSingleton<IContentMetadataReader, PdfDocumentMetadataReader>();
        services.AddSingleton<IContentMetadataReader, EpubMetadataReader>();
        services.AddSingleton<IContentMetadataReader, ImageMetadataReader>();

        // Empreinte perceptuelle d'images (Inc 5).
        services.AddSingleton<IImagePerceptualHasher, ImageSharpPerceptualHasher>();
        return services;
    }
}
