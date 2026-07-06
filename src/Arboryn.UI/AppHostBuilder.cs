using System;
using System.IO;
using Arboryn.Application;
using Arboryn.Infrastructure;
using Arboryn.Infrastructure.Database;
using Arboryn.UI.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Arboryn.UI;

/// <summary>
/// Composition root : configure DI, logging Serilog, et la connexion SQLite.
/// La base est créée si absente, et migrée au démarrage par App.OnLaunched.
/// </summary>
internal static class AppHostBuilder
{
    /// <summary>
    /// Résout l'emplacement de la base (Inc 13, A2) et initialise le logging. Appelé en tout
    /// premier par <see cref="App"/>, avant l'acquisition du verrou d'écriture et l'application
    /// d'un éventuel import différé — donc avant toute ouverture de connexion.
    /// </summary>
    public static DatabaseLocationInfo ResolveLocation()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var arborynDir = Path.Combine(localAppData, "Arboryn");
        Directory.CreateDirectory(arborynDir);
        Directory.CreateDirectory(Path.Combine(arborynDir, "logs"));

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(arborynDir, "logs", "Arboryn-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        // Config minimale pour lire les clés Database:* (le host reconstruit sa propre config).
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var dbPath = DatabaseLocation.Resolve(
            localAppData,
            Environment.GetEnvironmentVariable(DatabaseLocation.EnvVariable),
            DatabaseLocation.ReadPointer(arborynDir),
            config["Database:FullPath"],
            config["Database:PathRelativeToLocalAppData"]);

        var dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        Log.Information("Base de données Arboryn : {DbPath}", dbPath);
        return new DatabaseLocationInfo(arborynDir, dbPath);
    }

    public static IHost Build(DatabaseLocationInfo location)
    {
        var connectionString = $"Data Source={location.DatabasePath};Cache=Shared";

        return Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureAppConfiguration((ctx, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddArborynInfrastructure(connectionString);
                services.AddArborynApplication();

                // Emplacement résolu de la base (Inc 13, A2) — consommé par SqliteCatalogTransfer
                // et l'UI Réglages.
                services.AddSingleton(location);

                // Volume actif partagé (Inc 9) — lu par tous les VMs, défini par le scan / la page Volumes.
                services.AddSingleton<Services.ActiveVolumeContext>();

                // ViewModels partagés (Singleton — survivent à la navigation).
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<VolumesViewModel>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<InventoryViewModel>();
                services.AddSingleton<NormalizeViewModel>();
                services.AddSingleton<TaxonomyViewModel>();
                services.AddSingleton<TriageViewModel>();
                services.AddSingleton<EnrichmentViewModel>();
                services.AddSingleton<EnrichmentReviewViewModel>();
                services.AddSingleton<ReplicationScopesViewModel>();
                services.AddSingleton<PlacementReviewViewModel>();
                services.AddSingleton<DatabaseSettingsViewModel>();
                services.AddSingleton<HistoryViewModel>();

                // Shell — instancié une fois, reçoit le ServiceProvider pour résoudre les pages enfants.
                services.AddSingleton<MainWindow>(sp => new MainWindow(sp));
            })
            .Build();
    }
}
