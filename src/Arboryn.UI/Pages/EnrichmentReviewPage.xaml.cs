using System;
using Arboryn.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Arboryn.UI.Pages;

public sealed partial class EnrichmentReviewPage : Page
{
    public EnrichmentReviewViewModel ViewModel { get; private set; } = null!;

    public EnrichmentReviewPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is IServiceProvider services)
        {
            ViewModel = services.GetRequiredService<EnrichmentReviewViewModel>();
            this.DataContext = ViewModel;
            await ViewModel.LoadAsync();
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();

    private async void OnAcceptAllClick(object sender, RoutedEventArgs e) => await ViewModel.AcceptAllAsync();

    private async void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EnrichmentCandidateRowItem row)
        {
            await ViewModel.AcceptAsync(row);
        }
    }

    private async void OnRejectClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EnrichmentCandidateRowItem row)
        {
            await ViewModel.RejectAsync(row);
        }
    }
}
