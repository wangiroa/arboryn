using System;
using System.IO;
using Arboryn.Application;
using Arboryn.Infrastructure;
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
    public static IHost Build()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var ArborynDir = Path.Combine(localAppData, "Arboryn");
        Directory.CreateDirectory(ArborynDir);
        Directory.CreateDirectory(Path.Combine(ArborynDir, "logs"));

        var dbPath = Path.Combine(ArborynDir, "index.db");
        var connectionString = $"Data Source={dbPath};Cache=Shared";

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(ArborynDir, "logs", "Arboryn-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

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

                // Shell — instancié une fois, reçoit le ServiceProvider pour résoudre les pages enfants.
                services.AddSingleton<MainWindow>(sp => new MainWindow(sp));
            })
            .Build();
    }
}
