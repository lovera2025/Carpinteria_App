using System.Windows;
using System.Windows.Threading;
using MetroCarpinteria.App.Services;
using Velopack;

namespace MetroCarpinteria.App;

public partial class App : Application
{
    /// <summary>
    /// Entrada de la aplicación. Existe a mano (en vez del Main que genera WPF) porque
    /// Velopack tiene que correr su bootstrap antes que nada: durante una instalación o
    /// una actualización el ejecutable se invoca con parámetros especiales y tiene que
    /// poder atenderlos y salir sin llegar a abrir una ventana.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        AppHost.Initialize();

        StartUpdateCheckInBackground();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppHost.RunBackupOnExitIfEnabled();

        // Después del respaldo: si algo salió mal actualizando, los datos ya están a salvo.
        AppHost.ApplyPendingUpdateOnExit();

        base.OnExit(e);
    }

    /// <summary>
    /// Busca actualizaciones sin hacer esperar a nadie. Si no hay internet no pasa nada
    /// y la app abre igual, que es lo normal en el taller.
    /// </summary>
    private static void StartUpdateCheckInBackground()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var update = await AppHost.UpdateService.CheckAndDownloadOnStartupAsync();
                if (update is null)
                {
                    return;
                }

                // El aviso se pinta en la ventana, así que vuelve al hilo de UI.
                Current?.Dispatcher.Invoke(() =>
                {
                    if (Current?.MainWindow?.DataContext is ViewModels.MainViewModel main)
                    {
                        main.NotifyUpdateReady(update.TargetFullRelease.Version.ToString());
                    }
                });
            }
            catch
            {
                // Una actualización que no se pudo bajar no es asunto del usuario.
            }
        });
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
