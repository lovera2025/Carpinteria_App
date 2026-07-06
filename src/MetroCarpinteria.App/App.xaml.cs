using System.Windows;
using System.Windows.Threading;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        AppHost.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppHost.RunBackupOnExitIfEnabled();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Ocurrió un error inesperado:\n\n{e.Exception.Message}",
            "Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            MessageBox.Show(
                $"Ocurrió un error crítico:\n\n{ex.Message}",
                "Error crítico",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
