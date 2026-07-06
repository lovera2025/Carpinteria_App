using System.Windows.Controls;
using MetroCarpinteria.App.ViewModels;

namespace MetroCarpinteria.App.Views;

public partial class CashRegisterView : UserControl
{
    public CashRegisterView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is CashRegisterViewModel viewModel)
        {
            viewModel.Load();
        }
    }
}
