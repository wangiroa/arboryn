using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

    public EnrichmentViewModel Enrichment { get; private set; } = null!;

    public DatabaseSettingsViewModel Database { get; private set; } = null!;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is IServiceProvider services)
        {
            ViewModel = services.GetRequiredService<MainViewModel>();
            Enrichment = services.GetRequiredService<EnrichmentViewModel>();
            Database = services.GetRequiredService<DatabaseSettingsViewModel>();
            this.DataContext = ViewModel;
            await Enrichment.LoadAsync();
        }
    }

    private async void OnChooseDbLocationClick(object sender, RoutedEventArgs e)
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
            Database.ChooseLocation(folder.Path);
            await PromptRestartAsync();
        }
    }

    private async void OnResetDbLocationClick(object sender, RoutedEventArgs e)
    {
        Database.ResetToDefault();
        await PromptRestartAsync();
    }

    private async void OnExportDbClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
            SuggestedFileName = "arboryn-index",
        };
        picker.FileTypeChoices.Add("Base Arboryn", new List<string> { ".db" });
        var window = (Microsoft.UI.Xaml.Application.Current as App)?.RootShell;
        if (window is null)
        {
            return;
        }
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await Database.ExportAsync(file.Path);
        }
    }

    private async void OnImportDbClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".db");
        var window = (Microsoft.UI.Xaml.Application.Current as App)?.RootShell;
        if (window is null)
        {
            return;
        }
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            Database.ScheduleImport(file.Path);
            await PromptRestartAsync();
        }
    }

    /// <summary>Propose de fermer Arboryn pour appliquer un changement d'emplacement/import de base.</summary>
    private async Task PromptRestartAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Redémarrage requis",
            Content = "Ce changement prend effet au prochain démarrage. Fermer Arboryn maintenant ?",
            PrimaryButtonText = "Fermer maintenant",
            CloseButtonText = "Plus tard",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
    }

    private async void OnSaveEnrichmentClick(object sender, RoutedEventArgs e) => await Enrichment.SaveAsync();

    private async void OnEnrichCatalogClick(object sender, RoutedEventArgs e) => await Enrichment.EnrichCatalogAsync();

    private async void OnEnrichFolderClick(object sender, RoutedEventArgs e)
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
            await Enrichment.EnrichFolderAsync(folder.Path);
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
