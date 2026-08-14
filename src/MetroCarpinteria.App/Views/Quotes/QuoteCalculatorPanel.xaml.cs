using System.Windows;
using System.Windows.Controls;
using MetroCarpinteria.App.ViewModels;

namespace MetroCarpinteria.App.Views.Quotes;

public partial class QuoteCalculatorPanel : UserControl
{
    public QuoteCalculatorPanel()
    {
        InitializeComponent();
    }

    private void OnCommitmentLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is QuotesViewModel viewModel)
        {
            viewModel.SaveCommitmentNote();
        }
    }
}
