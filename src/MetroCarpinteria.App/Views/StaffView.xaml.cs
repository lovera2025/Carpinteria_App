using System.Windows.Controls;
using MetroCarpinteria.App.ViewModels;

namespace MetroCarpinteria.App.Views;

public partial class StaffView : UserControl
{
    public StaffView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is StaffViewModel vm)
            {
                vm.Load();
            }
        };
    }
}
