using System;
using Arboryn.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Arboryn.UI.Pages;

public sealed partial class SettingsPage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is IServiceProvider services)
        {
            ViewModel = services.GetRequiredService<MainViewModel>();
            this.DataContext = ViewModel;
        }
    }

    private async void OnAddPriorityFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        var window = (Microsoft.UI.Xaml.Application.Current as App)?.RootShell;
        if (window is null)
        {
            return;
        }
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.AddPriorityDirectory(folder.Path);
        }
    }

    private void OnAddSuggestionClick(object sender, RoutedEventArgs e)
    {
        if (SuggestionsCombo.SelectedItem is string directory)
        {
            ViewModel.AddPriorityDirectory(directory);
        }
    }

    private void OnAddPriorityPatternClick(object sender, RoutedEventArgs e)
    {
        ViewModel.AddPriorityDirectory(PriorityPatternBox.Text);
        PriorityPatternBox.Text = string.Empty;
    }

    private void OnPriorityPatternKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.AddPriorityDirectory(PriorityPatternBox.Text);
            PriorityPatternBox.Text = string.Empty;
        }
    }

    private async void OnAddExcludeFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        var window = (Microsoft.UI.Xaml.Application.Current as App)?.RootShell;
        if (window is null)
        {
            return;
        }
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.AddExcludedDirectory(folder.Path);
        }
    }

    private void OnAddExcludePatternClick(object sender, RoutedEventArgs e)
    {
        ViewModel.AddExcludedDirectory(ExcludePatternBox.Text);
        ExcludePatternBox.Text = string.Empty;
    }

    private void OnExcludePatternKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.AddExcludedDirectory(ExcludePatternBox.Text);
            ExcludePatternBox.Text = string.Empty;
        }
    }

    private void OnExcludeRemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string directory)
        {
            ViewModel.RemoveExcludedDirectory(directory);
        }
    }

    private void OnPriorityUpClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string directory)
        {
            ViewModel.MovePriorityUp(directory);
        }
    }

    private void OnPriorityDownClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string directory)
        {
            ViewModel.MovePriorityDown(directory);
        }
    }

    private void OnPriorityRemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string directory)
        {
            ViewModel.RemovePriorityDirectory(directory);
        }
    }

    private async void OnClearCatalogClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Vider le catalogue ?",
            Content = "Toutes les FileInstances et LogicalFiles seront supprimés de la base de données. Les fichiers sur disque ne sont pas affectés. Cette opération n'est pas réversible depuis l'application.",
            PrimaryButtonText = "Vider",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ClearCatalogAsync();
        }
    }
}
