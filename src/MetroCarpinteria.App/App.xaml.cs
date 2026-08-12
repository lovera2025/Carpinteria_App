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

    /// <summary>
    /// El orden importa y cada paso está donde está por un motivo:
    /// el log tiene que existir antes que nada porque el error que más interesa registrar
    /// es que la inicialización falle; la cultura va antes de crear ventanas para que los
    /// primeros formatos ya salgan bien; y la ventana principal se abre a mano —no con
    /// <c>StartupUri</c>— para poder no abrirla si los servicios no quedaron listos.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LogService.Bootstrap();
        Helpers.AppCulture.InstallAsProcessCulture();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            AppHost.Initialize();
        }
        catch (Exception ex)
        {
            LogService.Error("App.OnStartup", "Falló la inicialización", ex);

            // Con StartupUri la ventana principal abría igual y cada binding contra un
            // servicio en null tiraba otra excepción: el usuario terminaba con una cascada
            // de errores y sin saber cuál había sido el primero.
            new Views.StartupFailureWindow(ex).ShowDialog();
            Shutdown(1);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();

        // Después de Show: los avisos flotantes viven en la ventana principal.
        NotifyIfSettingsWereRecovered();

        StartUpdateCheckInBackground();
    }

    /// <summary>
    /// Si la configuración estaba ilegible se arrancó con los valores por defecto. Hay
    /// que decirlo: si no, los porcentajes del taller vuelven a los recomendados y el
    /// primer presupuesto del día sale con otro precio sin que nadie sepa por qué.
    /// </summary>
    private static void NotifyIfSettingsWereRecovered()
    {
        if (AppHost.SettingsService.CorruptFileName is not { } fileName)
        {
            return;
        }

        AppHost.NotificationService.Warning(
            "No se pudo leer la configuración guardada, así que se arrancó con los valores " +
            $"recomendados. El archivo anterior quedó como «{fileName}».");
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
        LogService.Error("Dispatcher", "Excepción no controlada en la interfaz", e.Exception);

        // Seguir vivo solo tiene sentido si los servicios están completos. Si el arranque
        // no terminó, cada acción posterior produciría otro error distinto y el usuario
        // nunca vería cuál fue el problema real: mejor cerrar con un mensaje claro.
        if (!AppHost.IsReady)
        {
            e.Handled = true;
            new Views.StartupFailureWindow(e.Exception).ShowDialog();
            Shutdown(1);
            return;
        }

        MessageBox.Show(
            $"Ocurrió un error inesperado:\n\n{e.Exception.Message}\n\n" +
            $"El detalle quedó guardado en:\n{LogService.CurrentFile}",
            "Metro Carpintería",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    /// <summary>
    /// Excepciones de tareas en segundo plano que nadie esperó. Faltaba engancharlo, y es
    /// justo donde caería un fallo del chequeo de actualizaciones.
    /// </summary>
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogService.Error("Task", "Excepción en una tarea en segundo plano", e.Exception);

        // Observada = no tumba el proceso. Ya quedó registrada, y no es algo
        // sobre lo que el usuario pueda hacer nada.
        e.SetObserved();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogService.Error("AppDomain", "Excepción crítica", ex);

            MessageBox.Show(
                $"Ocurrió un error crítico:\n\n{ex.Message}\n\n" +
                $"El detalle quedó guardado en:\n{LogService.CurrentFile}",
                "Metro Carpintería",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
