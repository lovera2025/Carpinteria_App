using System.Windows.Controls;
using MetroCarpinteria.App.ViewModels;

namespace MetroCarpinteria.App.Views;

public partial class QuotesView : UserControl
{
    public QuotesView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is QuotesViewModel vm)
            {
                vm.Load();
            }
        };
    }
}
