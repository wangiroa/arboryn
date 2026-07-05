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
        services.AddSingleton<IAudioFingerprintStore>(sp => sp.GetRequiredService<SqliteFileInstanceRepository>());
        services.AddSingleton<ILogicalFileRepository, SqliteLogicalFileRepository>();
        // Volumes (Inc 9) — dépôt + identification stable (VSN NTFS, empreinte SMB, marqueur .Arboryn)
        // + lecture USN Journal pour le re-scan incrémental (best-effort, repli mtime si indisponible).
        services.AddSingleton<IVolumeRepository, SqliteVolumeRepository>();
        // Périmètres de réplication (Inc 10) — expression de scope par volume (table replication_scopes)
        // + lecture du catalogue logique (œuvres + instances par volume) pour le calcul de placement.
        services.AddSingleton<IReplicationScopeRepository, SqliteReplicationScopeRepository>();
        services.AddSingleton<IReplicationCatalogReader, SqliteReplicationCatalogReader>();
        services.AddSingleton<IVolumeIdentifier, WindowsVolumeIdentifier>();
        services.AddSingleton<IUsnJournalReader, WindowsUsnJournalReader>();
        services.AddSingleton<IFileMetadataRepository, SqliteFileMetadataRepository>();
        services.AddSingleton<IOperationJournal, SqliteOperationJournal>();
        services.AddSingleton<IRecycleBin, RecycleBin>();
        services.AddSingleton<IFileMover, FileSystemMover>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        services.AddSingleton<IFileHasher, Sha256FileHasher>();

        // Lecteurs de métadonnées de contenu (Inc 4) — appliqués par catégorie.
        services.AddSingleton<IContentMetadataReader, TagLibAudioMetadataReader>();
        services.AddSingleton<IContentMetadataReader, PdfDocumentMetadataReader>();
        services.AddSingleton<IContentMetadataReader, EpubMetadataReader>();
        services.AddSingleton<IContentMetadataReader, ImageMetadataReader>();

        // Outils externes embarqués (fpcalc, ffmpeg) : tools/ → PATH.
        services.AddSingleton(new ExternalToolResolver(AppContext.BaseDirectory));

        // Empreintes perceptuelles (Inc 5) — registre par catégorie : image (pHash),
        // vidéo (pHash agrégé des keyframes via ffmpeg).
        services.AddSingleton<IPerceptualHasher, ImageSharpPerceptualHasher>();
        services.AddSingleton<IVideoKeyframeExtractor, FfmpegKeyframeExtractor>();
        services.AddSingleton<IPerceptualHasher, VideoPerceptualHasher>();

        // Empreinte acoustique (Inc 5) — fpcalc embarqué (tools/) ou présent dans le PATH.
        services.AddSingleton<IAudioFingerprinter, FpcalcAudioFingerprinter>();

        // Taxonomie canonique (Inc 6) — moteur de templates + dépôt.
        services.AddSingleton<ITemplateRenderer, Templates.ScribanTemplateRenderer>();
        services.AddSingleton<ITaxonomyRepository, SqliteTaxonomyRepository>();

        // Write-back de métadonnées dans le fichier (Inc 6) — par catégorie.
        services.AddSingleton<IContentMetadataWriter, TagLibAudioMetadataWriter>();
        // Note : l'enregistrement des providers d'enrichissement (Inc 8) est plus bas.

        // Triage de documents (Inc 7) — extraction texte (PdfPig), miniatures (Magick.NET),
        // OCR (Tesseract, dégradé si absent), et persistance patterns/corrections.
        services.AddSingleton<IDocumentTextExtractor, PdfTextExtractor>();
        services.AddSingleton<IDocumentThumbnailRenderer, MagickThumbnailRenderer>();
        services.AddSingleton<IOcrEngine, TesseractOcrEngine>();
        services.AddSingleton<ITriageRepository, SqliteTriageRepository>();

        // Enrichissement online (Inc 8) — cache, trousseau de clés, et providers (HttpClient typés).
        services.AddSingleton<IApiCache, SqliteApiCache>();
        services.AddSingleton<IEnrichmentCandidateRepository, SqliteEnrichmentCandidateRepository>();
        services.AddSingleton<IEnrichmentKeyring, Enrichment.SettingsEnrichmentKeyring>();
        AddProvider<Enrichment.OpenLibraryProvider>(services);
        AddProvider<Enrichment.GoogleBooksProvider>(services);
        AddProvider<Enrichment.TmdbProvider>(services);
        AddProvider<Enrichment.MusicBrainzProvider>(services);
        return services;
    }

    /// <summary>
    /// Enregistre un provider d'enrichissement avec son HttpClient typé (timeout + User-Agent
    /// — requis par MusicBrainz et courtois pour les autres) et l'expose comme
    /// <see cref="IMetadataProvider"/> pour l'orchestrateur.
    /// </summary>
    private static void AddProvider<TProvider>(IServiceCollection services)
        where TProvider : class, IMetadataProvider
    {
        services.AddHttpClient<TProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Arboryn/1.0 (+https://github.com/arboryn)");
        });
        services.AddTransient<IMetadataProvider>(sp => sp.GetRequiredService<TProvider>());
    }
}
