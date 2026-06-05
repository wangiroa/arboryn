using System;
using Arboryn.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

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
}
