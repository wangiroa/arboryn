using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arboryn.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Arboryn.UI.Pages;

public sealed partial class DuplicatesPage : Page
{
    private IServiceProvider? _services;

    public MainViewModel ViewModel { get; private set; } = null!;

    public DuplicatesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is IServiceProvider services)
        {
            _services = services;
            ViewModel = services.GetRequiredService<MainViewModel>();
            this.DataContext = ViewModel;
            ViewModel.PropertyChanged += OnVmChanged;
            UpdateSummary();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged -= OnVmChanged;
        }
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Groups) || e.PropertyName == nameof(MainViewModel.StatusText))
        {
            DispatcherQueue.TryEnqueue(UpdateSummary);
        }
    }

    private void UpdateSummary()
    {
        var count = ViewModel.Groups.Count;
        SummaryText.Text = count == 0
            ? "Lancez une détection sur un dossier pour faire apparaître les groupes."
            : $"{count} groupe(s) détecté(s).";
    }

    public void RequestCancelScan() => ViewModel.CancelScan();

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
            await ViewModel.ScanAndDetectAsync(folder.Path);
        }
    }

    private async void OnRescanClick(object sender, RoutedEventArgs e) => await ViewModel.RescanAsync();

    private void OnCancelClick(object sender, RoutedEventArgs e) => ViewModel.CancelScan();

    private async void OnDetectFuzzyClick(object sender, RoutedEventArgs e) => await ViewModel.DetectFuzzyAsync();

    private async void OnConfirmHashClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DuplicateGroupItem group })
        {
            await ViewModel.ConfirmByHashAsync(group);
        }
    }

    private async void OnUndoClick(object sender, RoutedEventArgs e) => await ViewModel.UndoLastDeleteAsync();

    private async void OnDeleteGroupClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DuplicateGroupItem group })
        {
            return;
        }
        var selected = group.Members.Where(m => m.ShouldDelete).ToList();
        if (await ConfirmDeleteAsync(selected))
        {
            await ViewModel.DeleteSelectedAsync(group);
        }
    }

    private async void OnDeleteAllClick(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.Groups.SelectMany(g => g.Members).Where(m => m.ShouldDelete).ToList();
        if (await ConfirmDeleteAsync(selected))
        {
            await ViewModel.DeleteAllSelectedAsync();
        }
    }

    private async Task<bool> ConfirmDeleteAsync(IReadOnlyList<DuplicateMemberItem> selected)
    {
        if (selected.Count == 0)
        {
            return false;
        }
        var totalSize = selected.Sum(m => m.Size);
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Confirmer la suppression",
            Content = $"Envoyer {selected.Count} fichier(s) ({SizeFormatter.Humanize(totalSize)}) à la corbeille ?",
            PrimaryButtonText = "Supprimer",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
