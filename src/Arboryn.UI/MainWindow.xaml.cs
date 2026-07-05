using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arboryn.Application.Inventory;
using Arboryn.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace Arboryn.UI;

public sealed partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, Type> _pages;
    private string? _currentRoute;
    private AppWindow? _appWindow;

    public MainWindow(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ApplyAppIcon();
        ConfigureTitleBarInsets();

        _pages = new Dictionary<string, Type>
        {
            ["dashboard"] = typeof(Pages.DashboardPage),
            ["volumes"] = typeof(Pages.VolumesPage),
            ["catalog"] = typeof(Pages.CatalogPage),
            ["duplicates"] = typeof(Pages.DuplicatesPage),
            ["normalize"] = typeof(Pages.NormalizePage),
            ["triage"] = typeof(Pages.TriagePage),
            ["review"] = typeof(Pages.EnrichmentReviewPage),
            ["replication"] = typeof(Pages.ReplicationScopesPage),
            ["placement"] = typeof(Pages.PlacementPage),
            ["taxonomy"] = typeof(Pages.TaxonomyPage),
            ["history"] = typeof(Pages.HistoryPage),
            ["settings"] = typeof(Pages.SettingsPage),
        };

        ContentFrame.NavigationFailed += (_, args)
            => throw new InvalidOperationException($"Navigation vers {args.SourcePageType.FullName} en échec.", args.Exception);

        // Pré-sélectionne le tableau de bord (déclenche aussi SelectionChanged).
        ShellNav.Loaded += (_, _) =>
        {
            if (ShellNav.MenuItems.Count > 0 && ShellNav.MenuItems[0] is NavigationViewItem first)
            {
                ShellNav.SelectedItem = first;
            }
        };
    }

    public IServiceProvider Services => _services;

    /// <summary>
    /// Applique l'icône de marque Arboryn à la fenêtre (titre, taskbar, alt-tab).
    /// L'icône PNG-in-ICO multi-résolutions est générée par Assets/generate-app-icon.ps1
    /// à partir du même tracé que Controls.BrandMark.
    /// </summary>
    private void ApplyAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Arboryn.ico");
        if (!File.Exists(iconPath))
        {
            return;
        }
        AppWindowRef().SetIcon(iconPath);
    }

    private AppWindow AppWindowRef()
    {
        if (_appWindow is null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
        }

        return _appWindow;
    }

    /// <summary>
    /// Réserve, à droite de la barre de titre personnalisée, la largeur des boutons système
    /// (réduire / agrandir / fermer) pour que les actions (thème, activité, compte) ne les
    /// recouvrent plus. La largeur (RightInset) est en pixels physiques → convertie en DIP.
    /// </summary>
    private void ConfigureTitleBarInsets()
    {
        AppTitleBar.SizeChanged += (_, _) => UpdateCaptionInset();
        AppTitleBar.Loaded += (_, _) => UpdateCaptionInset();
    }

    private void UpdateCaptionInset()
    {
        var scale = AppTitleBar.XamlRoot?.RasterizationScale ?? 1.0;
        var rightInsetDip = AppWindowRef().TitleBar.RightInset / scale;
        TrailingActions.Margin = new Thickness(0, 0, rightInsetDip, 0);
    }

    /// <summary>
    /// Sélectionne et active la route demandée dans la NavigationView (appelée
    /// depuis les pages enfants pour les CTA inter-écrans).
    /// </summary>
    public void SelectRoute(string route)
    {
        foreach (var item in ShellNav.MenuItems)
        {
            if (item is NavigationViewItem nav && nav.Tag is string tag && tag == route)
            {
                ShellNav.SelectedItem = nav;
                return;
            }
        }
        foreach (var item in ShellNav.FooterMenuItems)
        {
            if (item is NavigationViewItem nav && nav.Tag is string tag && tag == route)
            {
                ShellNav.SelectedItem = nav;
                return;
            }
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item && item.Tag is string route)
        {
            NavigateTo(route);
        }
    }

    private void NavigateTo(string route)
    {
        if (route == _currentRoute)
        {
            return;
        }
        if (!_pages.TryGetValue(route, out var pageType))
        {
            return;
        }

        ContentFrame.Navigate(pageType, _services, new SuppressNavigationTransitionInfo());
        _currentRoute = route;
    }

    /// <summary>
    /// Recherche cross-volume « où est X ? » (Inc 11) : à la frappe, propose les œuvres dont le
    /// nom/chemin correspond, avec la liste des volumes où chacune est présente.
    /// </summary>
    private async void OnUniversalSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        var query = sender.Text?.Trim() ?? string.Empty;
        if (query.Length < 2)
        {
            sender.ItemsSource = null;
            return;
        }

        try
        {
            var handler = _services.GetRequiredService<CrossVolumeSearchHandler>();
            var results = await handler.SearchAsync(query, maxResults: 12).ConfigureAwait(true);
            sender.ItemsSource = results.Select(r => new UniversalSearchItem(r)).ToList();
        }
        catch
        {
            sender.ItemsSource = null;
        }
    }

    private void OnCancelScanClick(object sender, RoutedEventArgs e)
    {
        // Délégation à la page Doublons si visible.
        if (ContentFrame.Content is Pages.DuplicatesPage page)
        {
            page.RequestCancelScan();
        }
    }

    public void SetScanVisible(bool visible) => ScanBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void UpdateScanProgress(string volumeLabel, double percent, string etaText)
    {
        ScanVolumeLabel.Text = volumeLabel;
        ScanProgress.Value = percent;
        ScanEtaLabel.Text = etaText;
    }

    public void SetActivityChipVisible(bool visible) => ActivityChip.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
}
