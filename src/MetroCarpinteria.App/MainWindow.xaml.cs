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

        // Mientras la máquina duerme los temporizadores no corren, así que volver a la
        // ventana es el momento en que más probable es que el día haya cambiado sin que
        // nadie se enterara.
        Activated += (_, _) => AppHost.ClockService.CheckForDayChange();

        AppHost.ClockService.Start();
    }
}
