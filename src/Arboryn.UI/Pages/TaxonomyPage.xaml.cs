using System;
using Arboryn.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Arboryn.UI.Pages;

public sealed partial class TaxonomyPage : Page
{
    public TaxonomyViewModel ViewModel { get; private set; } = null!;

    public TaxonomyPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is IServiceProvider services)
        {
            ViewModel = services.GetRequiredService<TaxonomyViewModel>();
            this.DataContext = ViewModel;
            await ViewModel.LoadAsync();
        }
    }

    private void OnPreviewClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TaxonomyItem item })
        {
            ViewModel.Preview(item);
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TaxonomyItem item })
        {
            await ViewModel.SaveAsync(item);
        }
    }
}
