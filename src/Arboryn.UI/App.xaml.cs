using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

namespace Arboryn.UI;

public partial class App : Microsoft.UI.Xaml.Application
{
    public IHost Host { get; private set; } = null!;
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    /// <summary>Fenêtre coque active — utilisé par les pages enfants pour la navigation inter-écrans.</summary>
    public MainWindow? RootShell => _window;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Host = AppHostBuilder.Build();
        await Host.StartAsync().ConfigureAwait(true);

        // Application des migrations SQLite au démarrage
        var migrator = Host.Services.GetRequiredService<Arboryn.Infrastructure.Database.Migrator>();
        await migrator.ApplyMigrationsAsync().ConfigureAwait(true);

        // Met à niveau les taxonomies stockées qui ne sont que d'anciens défauts livrés, afin
        // que les améliorations des templates par défaut atteignent les bases existantes
        // (les personnalisations utilisateur sont préservées). Idempotent.
        var taxonomyUpgrader = Host.Services
            .GetRequiredService<Application.UseCases.UpgradeDefaultTaxonomiesHandler>();
        await taxonomyUpgrader.ExecuteAsync(CancellationToken.None).ConfigureAwait(true);

        // Rattache les FileInstances éventuellement orphelines (Inc 3) à des LogicalFiles
        // avant tout autre traitement. Idempotent — sans coût si tout est déjà rattaché.
        var logicalFiles = Host.Services.GetRequiredService<Application.Abstractions.ILogicalFileRepository>();
        await logicalFiles.BackfillUnattachedAsync(CancellationToken.None).ConfigureAwait(true);

        // Charge le trousseau de clés d'API d'enrichissement (Inc 8) en mémoire pour un accès
        // synchrone par les providers (ex. TMDB.IsConfigured).
        var keyring = Host.Services.GetRequiredService<Application.Abstractions.IEnrichmentKeyring>();
        await keyring.RefreshAsync(CancellationToken.None).ConfigureAwait(true);

        // Chargement des préférences (répertoires prioritaires) avant l'affichage.
        var viewModel = Host.Services.GetRequiredService<ViewModels.MainViewModel>();
        await viewModel.LoadSettingsAsync().ConfigureAwait(true);

        _window = Host.Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }
}
