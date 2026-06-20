using System;
using Arboryn.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Arboryn.UI.Pages;

public sealed partial class CatalogPage : Page
{
    public InventoryViewModel ViewModel { get; private set; } = null!;

    public CatalogPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is IServiceProvider services)
        {
            ViewModel = services.GetRequiredService<InventoryViewModel>();
            this.DataContext = ViewModel;
            await ViewModel.LoadAsync().ConfigureAwait(true);
        }
    }

    private void OnResetFilters(object sender, RoutedEventArgs e)
        => ViewModel.ResetFilters();

    private async void OnPickDirectory(object sender, RoutedEventArgs e)
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
            ViewModel.SetDirectoryFilter(folder.Path);
        }
    }

    private void OnClearDirectory(object sender, RoutedEventArgs e)
        => ViewModel.ClearDirectoryFilter();
}
