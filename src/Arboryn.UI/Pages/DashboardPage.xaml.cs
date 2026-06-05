using System;
using Arboryn.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Arboryn.UI.Pages;

public sealed partial class DashboardPage : Page
{
    private DashboardViewModel? _viewModel;
    private IServiceProvider? _services;

    public DashboardPage()
    {
        InitializeComponent();
        BuildTodos();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is IServiceProvider services)
        {
            _services = services;
            _viewModel = services.GetRequiredService<DashboardViewModel>();
            _viewModel.PropertyChanged += OnViewModelChanged;
            await _viewModel.LoadAsync().ConfigureAwait(true);
            RefreshTexts();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(RefreshTexts);
    }

    private void RefreshTexts()
    {
        if (_viewModel is null)
        {
            return;
        }
        LogicalFilesText.Text = _viewModel.LogicalFileCountText;
        FileInstancesText.Text = _viewModel.FileInstanceCountText;
        RedundancyText.Text = _viewModel.RedundancyText;
        SubtitleText.Text = _viewModel.StatusLine;
        LastScanText.Text = _viewModel.LogicalFileCount == 0 ? "Aucun" : "à l'instant";
    }

    private void BuildTodos()
    {
        // Suggestions actionnables — chaque carte mène à la page concernée.
        AddTodo("", "Rechercher des doublons", "Lance une détection sur le dossier de ton choix.", "duplicates");
        AddTodo("", "Explorer le catalogue", "Liste des LogicalFiles et de leurs copies physiques.", "catalog");
        AddTodo("", "Préparer le multi-volume", "Marqueur .Arboryn, identification stable, scopes de réplication.", "volumes");
        AddTodo("", "Configurer les options de scan", "Priorité des dossiers, seuil flou, préférence de copie.", "settings");
    }

    private void AddTodo(string glyph, string title, string description, string route)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["ArborynCardSecondaryBrush"],
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnSpacing = 12;

        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["ArborynAccentSelectedBgBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontFamily = (FontFamily)Microsoft.UI.Xaml.Application.Current.Resources["ArborynIconFontFamily"],
            FontSize = 16,
            Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["ArborynAccentTextBrush"],
        };
        iconBorder.Child = icon;
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        var stack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var titleBlock = new TextBlock
        {
            Text = title,
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["ArborynBodyStrongText"],
            Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["ArborynTextPrimaryBrush"],
        };
        var descBlock = new TextBlock
        {
            Text = description,
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["ArborynCaptionText"],
            Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["ArborynTextSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
        stack.Children.Add(titleBlock);
        stack.Children.Add(descBlock);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        var ctaButton = new Button
        {
            Content = "Ouvrir",
            VerticalAlignment = VerticalAlignment.Center,
            Tag = route,
        };
        ctaButton.Click += (_, _) =>
        {
            // Délègue au shell via navigation Frame parent
            if (this.Frame?.Tag is null && App.Current is App app && app.Host is not null)
            {
                NavigateRoute(route);
            }
            else
            {
                NavigateRoute(route);
            }
        };
        Grid.SetColumn(ctaButton, 2);
        grid.Children.Add(ctaButton);

        border.Child = grid;
        TodosPanel.Children.Add(border);
    }

    private void NavigateRoute(string route)
    {
        if (App.Current is App app)
        {
            // Cherche la NavigationView dans la fenêtre principale et sélectionne l'item correspondant.
            if (app.RootShell is { } shell)
            {
                shell.SelectRoute(route);
            }
        }
    }

    private void OnRescanClick(object sender, RoutedEventArgs e) => NavigateRoute("duplicates");

    private void OnGoToHistoryClick(object sender, RoutedEventArgs e) => NavigateRoute("history");
}
