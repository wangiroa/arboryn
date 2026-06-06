using System;
using Arboryn.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Arboryn.UI.Pages;

public sealed partial class NormalizePage : Page
{
    public NormalizeViewModel ViewModel { get; private set; } = null!;

    public NormalizePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is IServiceProvider services)
        {
            ViewModel = services.GetRequiredService<NormalizeViewModel>();
            this.DataContext = ViewModel;
        }
    }

    private async void OnPickFolderClick(object sender, RoutedEventArgs e)
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
            ViewModel.SelectFolder(folder.Path);
        }
    }

    private async void OnPlanClick(object sender, RoutedEventArgs e) => await ViewModel.PlanAsync();

    private async void OnExecuteClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Confirmer l'uniformisation",
            Content = $"Déplacer / renommer {ViewModel.PendingOperationCount} fichier(s) vers leurs chemins canoniques ? L'opération est annulable.",
            PrimaryButtonText = "Uniformiser",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ExecuteAsync();
        }
    }

    private async void OnUndoClick(object sender, RoutedEventArgs e) => await ViewModel.UndoAsync();
}
