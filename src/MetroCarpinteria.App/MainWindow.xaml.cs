using MetroCarpinteria.App.Services;
using MetroCarpinteria.App.ViewModels;
using System.Windows;

namespace MetroCarpinteria.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
