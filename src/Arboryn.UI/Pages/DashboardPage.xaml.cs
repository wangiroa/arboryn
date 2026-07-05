using System;
using System.Collections.Generic;
using Arboryn.Application.Abstractions;
using Arboryn.Application.Inventory;
using Arboryn.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Arboryn.UI.Pages;

public sealed partial class DashboardPage : Page
{
    private IServiceProvider? _services;

    public DashboardViewModel ViewModel { get; private set; } = null!;

    public DashboardPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is IServiceProvider services)
        {
            _services = services;
            ViewModel = services.GetRequiredService<DashboardViewModel>();
            this.DataContext = ViewModel;
            Bindings.Update();
            await ViewModel.LoadAsync();
        }
    }

    private void OnRescanClick(object sender, RoutedEventArgs e)
    {
        if (Microsoft.UI.Xaml.Application.Current is App { RootShell: { } shell })
        {
            shell.SelectRoute("duplicates");
        }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_services is null || (Microsoft.UI.Xaml.Application.Current as App)?.RootShell is not { } window)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "inventaire-arboryn",
        };
        picker.FileTypeChoices.Add("CSV (matrice)", new List<string> { ".csv" });
        picker.FileTypeChoices.Add("JSON (complet)", new List<string> { ".json" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        var export = await _services.GetRequiredService<InventoryExportHandler>().BuildAsync();
        var content = file.FileType.Equals(".json", StringComparison.OrdinalIgnoreCase) ? export.Json : export.Csv;
        await FileIO.WriteTextAsync(file, content);
    }

    private async void OnVolumeDetailsClick(object sender, RoutedEventArgs e)
    {
        if (_services is null || sender is not FrameworkElement { DataContext: VolumeMatrixRow row })
        {
            return;
        }

        var detail = await _services.GetRequiredService<VolumeDrillDownHandler>().ExecuteAsync(row.Id);

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(Section($"Manque sur ce volume (à copier) — {detail.Missing.Count}", detail.Missing));
        content.Children.Add(Section($"Surplus (hors périmètre, à retirer) — {detail.Surplus.Count}", detail.Surplus));

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = $"Détail du volume « {row.Name} »",
            Content = new ScrollViewer { Content = content, MaxHeight = 480 },
            CloseButtonText = "Fermer",
        };
        await dialog.ShowAsync();
    }

    private static UIElement Section(string title, IReadOnlyList<InventoryWorkItem> items)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["ArborynBodyStrongText"],
        });

        if (items.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Aucun.",
                Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["ArborynCaptionText"],
            });
            return panel;
        }

        foreach (var item in items)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"{CategoryLabels.Of(item.Category)} — {item.Name}",
                Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["ArborynCaptionText"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        return panel;
    }
}
