using System.Windows.Controls;
using MetroCarpinteria.App.ViewModels;

namespace MetroCarpinteria.App.Views;

public partial class ProjectsView : UserControl
{
    public ProjectsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is ProjectsViewModel vm)
            {
                vm.Load();
            }
        };
    }
}
