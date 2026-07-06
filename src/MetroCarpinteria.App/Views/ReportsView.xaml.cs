using System.Windows.Controls;
using MetroCarpinteria.App.ViewModels;

namespace MetroCarpinteria.App.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is ReportsViewModel vm)
            {
                vm.Load();
            }
        };
    }
}
