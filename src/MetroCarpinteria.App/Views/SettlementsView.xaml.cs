using System.Windows.Controls;
using MetroCarpinteria.App.ViewModels;

namespace MetroCarpinteria.App.Views;

public partial class SettlementsView : UserControl
{
    public SettlementsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is SettlementsViewModel vm)
            {
                vm.Load();
            }
        };
    }
}
