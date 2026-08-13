using System.Windows;
using System.Windows.Controls;
using MetroCarpinteria.App.ViewModels;

namespace MetroCarpinteria.App.Views.Quotes;

public partial class QuoteImagesPanel : UserControl
{
    public QuoteImagesPanel()
    {
        InitializeComponent();
    }

    private void OnCaptionLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: QuoteImageRow row })
        {
            return;
        }

        if (DataContext is QuotesViewModel viewModel
            && viewModel.SaveImageCaptionCommand.CanExecute(row))
        {
            viewModel.SaveImageCaptionCommand.Execute(row);
        }
    }
}
