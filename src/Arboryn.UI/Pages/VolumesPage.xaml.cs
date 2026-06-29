using System;
using Arboryn.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Arboryn.UI.Pages;

public sealed partial class VolumesPage : Page
{
    public VolumesViewModel ViewModel { get; private set; } = null!;

    public VolumesPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is IServiceProvider services)
        {
            ViewModel = services.GetRequiredService<VolumesViewModel>();
            this.DataContext = ViewModel;
            Bindings.Update();
            await ViewModel.LoadAsync();
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();

    private async void OnEnrollClick(object sender, RoutedEventArgs e)
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
            await ViewModel.EnrollFolderAsync(folder.Path);
        }
    }

    private void OnSetActiveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: VolumeRowItem row })
        {
            ViewModel.SetActive(row);
        }
    }
}
