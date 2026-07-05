using System;
using System.Collections.Generic;
using System.Linq;
using Arboryn.Domain.Enums;
using Arboryn.Domain.Replication;
using Arboryn.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Arboryn.UI.Pages;

public sealed partial class ReplicationScopesPage : Page
{
    public ReplicationScopesViewModel ViewModel { get; private set; } = null!;

    public ReplicationScopesPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is IServiceProvider services)
        {
            ViewModel = services.GetRequiredService<ReplicationScopesViewModel>();
            this.DataContext = ViewModel;
            Bindings.Update();
            await ViewModel.LoadAsync();
        }
    }

    private async void OnEditScopeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: VolumeScopeRow row })
        {
            return;
        }

        // Modes : Tout / Catégories précises / Aucun.
        var allRadio = new RadioButton { Content = "Tout le contenu", GroupName = "scope" };
        var categoriesRadio = new RadioButton { Content = "Catégories précises", GroupName = "scope" };
        var noneRadio = new RadioButton { Content = "Aucun (rien ne sera répliqué)", GroupName = "scope" };

        var current = row.Categories.ToHashSet();
        var checkboxes = CategoryLabels.ScopeCategories
            .Select(category => new CheckBox
            {
                Content = CategoryLabels.Of(category),
                IsChecked = current.Contains(category),
                Tag = category,
                Margin = new Thickness(24, 0, 0, 0),
            })
            .ToList();

        var categoryPanel = new StackPanel { Spacing = 2 };
        foreach (var checkbox in checkboxes)
        {
            categoryPanel.Children.Add(checkbox);
        }

        void SyncEnabled()
        {
            var enabled = categoriesRadio.IsChecked == true;
            foreach (var checkbox in checkboxes)
            {
                checkbox.IsEnabled = enabled;
            }
        }
        categoriesRadio.Checked += (_, _) => SyncEnabled();
        categoriesRadio.Unchecked += (_, _) => SyncEnabled();

        // Pré-sélection selon l'état courant.
        if (row.IsAll)
        {
            allRadio.IsChecked = true;
        }
        else if (row.Categories.Count > 0)
        {
            categoriesRadio.IsChecked = true;
        }
        else
        {
            noneRadio.IsChecked = true;
        }

        SyncEnabled();

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(allRadio);
        content.Children.Add(categoriesRadio);
        content.Children.Add(categoryPanel);
        content.Children.Add(noneRadio);

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = $"Périmètre de « {row.Name} »",
            Content = new ScrollViewer { Content = content },
            PrimaryButtonText = "Enregistrer",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ScopeExpression? expression;
        if (allRadio.IsChecked == true)
        {
            expression = ScopeExpression.All;
        }
        else if (categoriesRadio.IsChecked == true)
        {
            var selected = checkboxes
                .Where(cb => cb.IsChecked == true)
                .Select(cb => (MediaCategory)cb.Tag)
                .ToArray();
            expression = selected.Length > 0 ? ScopeExpression.Categories(selected) : null;
        }
        else
        {
            expression = null; // Aucun
        }

        await ViewModel.SetScopeAsync(row, expression);
    }
}
