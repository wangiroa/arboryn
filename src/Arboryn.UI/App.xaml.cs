using System.IO;
using System.Runtime.InteropServices;
using Arboryn.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

namespace Arboryn.UI;

public partial class App : Microsoft.UI.Xaml.Application
{
    public IHost Host { get; private set; } = null!;
    private MainWindow? _window;
    private FileStream? _dbLock;

    public App()
    {
        InitializeComponent();
    }

    /// <summary>Fenêtre coque active — utilisé par les pages enfants pour la navigation inter-écrans.</summary>
    public MainWindow? RootShell => _window;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Inc 13 A2 : résout l'emplacement de la base, verrouille l'écriture (une seule instance/PC
        // à la fois), puis applique un éventuel import différé — le tout AVANT toute ouverture de
        // connexion (migrations comprises).
        var location = AppHostBuilder.ResolveLocation();

        _dbLock = DatabaseWriteLock.TryAcquire(location.DatabasePath);
        if (_dbLock is null)
        {
            ShowDatabaseLockedMessage(location.DatabasePath);
            Exit();
            return;
        }

        PendingDatabaseImport.ApplyIfScheduled(location.ArborynDir, location.DatabasePath);

        Host = AppHostBuilder.Build(location);
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

        // Restaure le dernier volume actif (Inc 9), s'il existe encore en base.
        var activeVolume = Host.Services.GetRequiredService<Services.ActiveVolumeContext>();
        var volumeRepository = Host.Services.GetRequiredService<Application.Abstractions.IVolumeRepository>();
        await activeVolume.InitializeAsync(volumeRepository).ConfigureAwait(true);

        // Chargement des préférences (répertoires prioritaires) avant l'affichage.
        var viewModel = Host.Services.GetRequiredService<ViewModels.MainViewModel>();
        await viewModel.LoadSettingsAsync().ConfigureAwait(true);

        _window = Host.Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }

    /// <summary>
    /// Affiche un message d'erreur bloquant natif (aucune fenêtre XAML n'existe encore à ce stade
    /// du démarrage) quand la base est déjà ouverte ailleurs, puis l'app se ferme.
    /// </summary>
    private static void ShowDatabaseLockedMessage(string databasePath)
    {
        const uint mbIconError = 0x00000010;
        MessageBox(
            IntPtr.Zero,
            $"La base Arboryn est déjà ouverte (autre instance ou autre PC) :\n{databasePath}\n\n" +
            "Fermez-la avant de relancer l'application.",
            "Arboryn",
            mbIconError);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
