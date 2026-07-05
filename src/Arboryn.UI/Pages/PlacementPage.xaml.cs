using System;
using Arboryn.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Arboryn.UI.Pages;

public sealed partial class PlacementPage : Page
{
    public PlacementReviewViewModel ViewModel { get; private set; } = null!;

    public PlacementPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is IServiceProvider services)
        {
            ViewModel = services.GetRequiredService<PlacementReviewViewModel>();
            this.DataContext = ViewModel;
            Bindings.Update();
        }
    }

    private async void OnGenerateClick(object sender, RoutedEventArgs e) => await ViewModel.GenerateAsync();

    private async void OnExecuteClick(object sender, RoutedEventArgs e) => await ViewModel.ExecuteAsync();

    private async void OnUndoClick(object sender, RoutedEventArgs e) => await ViewModel.UndoAsync();
}
