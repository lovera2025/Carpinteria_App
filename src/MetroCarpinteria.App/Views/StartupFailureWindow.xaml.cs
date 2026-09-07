using System.Diagnostics;
using System.IO;
using System.Windows;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.Views;

/// <summary>
/// Lo que ve el usuario cuando el arranque no llegó a completarse.
/// <para>
/// Antes, si <c>AppHost.Initialize()</c> fallaba, el manejador global mostraba un
/// <c>MessageBox</c> y marcaba la excepción como atendida. Con <c>StartupUri</c> la ventana
/// principal abría igual, pero todos los servicios habían quedado en null: cada clic
/// producía otro error, y ninguno decía cuál había sido el problema original.
/// </para>
/// <para>
/// Esta ventana no toca <see cref="AppHost"/> ni <see cref="BackupService"/>, porque
/// cualquiera de los dos puede ser lo que está roto. Lee la carpeta de respaldos
/// directamente del disco.
/// </para>
/// </summary>
public partial class StartupFailureWindow : Window
{
    private readonly string _dataRoot;

    public StartupFailureWindow(Exception exception, string? dataRoot = null)
    {
        InitializeComponent();

        _dataRoot = dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MetroCarpinteria");

        MessageText.Text = Describe(exception);
        DetailText.Text = exception.ToString();

        LogPathText.Text = LogService.CurrentFile is { } log
            ? $"El detalle completo quedó guardado en:\n{log}"
            : "No se pudo escribir el archivo de registro.";

        // Actualizar resuelve un solo fallo de arranque: que la base sea más nueva que el
        // programa. Ofrecerlo en los otros sería mandar a alguien a perder el tiempo.
        if (exception is SchemaTooNewException)
        {
            UpdateButton.Visibility = Visibility.Visible;

            // Con cuatro botones la fila no entra en 640 y «Cerrar» se cae a un segundo
            // renglón, encimado. Se ensancha solo en este caso: los demás siguen con tres.
            Width = 820;
        }

        ShowBackupHint();
    }

    /// <summary>
    /// Busca la versión nueva, la baja y la instala reabriendo la app.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sin esto el cartel es un callejón sin salida: dice «actualizá antes de abrirla» y
    /// actualizar es justo lo que la app no puede hacer en ese estado, porque el chequeo
    /// de versiones corre <b>después</b> de abrir la base y acá nunca se llega.
    /// </para>
    /// <para>
    /// Se arma un <see cref="UpdateService"/> propio en vez de usar el de
    /// <see cref="AppHost"/>: el arranque quedó a medias y ese puede estar en null. Es la
    /// misma razón por la que esta ventana lee los respaldos del disco.
    /// </para>
    /// </remarks>
    private async void OnUpdateNow(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        var original = MessageText.Text;

        try
        {
            var updates = new UpdateService(new SettingsService(new AppPaths()));

            if (!updates.IsSupported)
            {
                MessageText.Text = original +
                    "\n\nEsta copia no se instaló con el instalador (es portable o corre desde " +
                    "el proyecto), así que no puede actualizarse sola. Hay que bajar la versión " +
                    "nueva a mano.";
                return;
            }

            MessageText.Text = "Buscando la versión nueva…";
            var check = await updates.CheckAsync();

            if (check is not { Outcome: UpdateCheckOutcome.Available, Update: not null })
            {
                // Los dos casos que quedan son distintos y decirlos igual haría perder
                // tiempo: si ya es la última, el problema no era la versión.
                MessageText.Text = original + (check.Outcome == UpdateCheckOutcome.UpToDate
                    ? "\n\nYa tenés la última versión publicada, así que actualizar no lo va a " +
                      "resolver. La base viene de una copia más nueva todavía: probá con un respaldo."
                    : "\n\nNo se pudo consultar si hay una versión nueva. Revisá la conexión y " +
                      "volvé a intentar.");
                UpdateButton.IsEnabled = true;
                return;
            }

            var version = check.Update.TargetFullRelease.Version;
            MessageText.Text = $"Bajando la versión {version}…";

            if (!await updates.DownloadAsync(check.Update))
            {
                MessageText.Text = original + "\n\nNo se pudo bajar la actualización. Revisá la conexión.";
                UpdateButton.IsEnabled = true;
                return;
            }

            if (!updates.ApplyAndRestart(check.Update))
            {
                MessageText.Text = original + "\n\nSe bajó la versión nueva pero no se pudo instalar.";
                UpdateButton.IsEnabled = true;
                return;
            }

            // El reemplazo de archivos pasa cuando este proceso termina, así que lo último
            // que hace la ventana es irse.
            MessageText.Text = $"Instalando la versión {version}. La app se abre sola en unos segundos.";
            Close();
        }
        catch (Exception ex)
        {
            LogService.Error("StartupFailureWindow", "Falló la actualización manual", ex);
            MessageText.Text = original + $"\n\nNo se pudo actualizar: {ex.Message}";
            UpdateButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Traduce lo que se pueda a algo accionable. Para el resto se muestra el mensaje
    /// original, que es mejor que un texto genérico que no ayuda a nadie.
    /// </summary>
    private static string Describe(Exception exception) => exception switch
    {
        SchemaTooNewException tooNew => tooNew.Message,

        UnauthorizedAccessException =>
            "Windows no dio permiso para leer o escribir en la carpeta de datos.\n\n" +
            "Suele pasar si la carpeta quedó sincronizada por OneDrive o si la app se abrió " +
            "con otro usuario de Windows.",

        IOException io when io.Message.Contains("being used", StringComparison.OrdinalIgnoreCase)
                         || io.Message.Contains("en uso", StringComparison.OrdinalIgnoreCase) =>
            "La base de datos está abierta por otro programa.\n\n" +
            "Fijate si quedó otra ventana de Metro Carpintería abierta y cerrala.",

        _ => exception.Message
    };

    private void ShowBackupHint()
    {
        var backups = ReadBackups();

        if (backups.Count == 0)
        {
            BackupHintText.Text = "No se encontraron respaldos en la carpeta de datos.";
            RestoreBackupButton.IsEnabled = false;
            return;
        }

        var newest = backups[0];
        BackupHintText.Text =
            $"Hay {backups.Count} respaldo(s). El más reciente es del " +
            $"{File.GetLastWriteTime(newest):dd/MM/yyyy HH:mm}.\n" +
            "Restaurar abre la carpeta para que puedas copiar el archivo sobre " +
            "«data\\carpinteria.db». Conviene guardar antes una copia del actual.";
    }

    /// <summary>
    /// Lista los respaldos leyendo el disco directo, sin pasar por <see cref="BackupService"/>.
    /// Los <c>pre_restore</c> quedan fuera: son copias de seguridad automáticas, no puntos
    /// de restauración elegidos por el usuario.
    /// </summary>
    private List<string> ReadBackups()
    {
        try
        {
            var directory = Path.Combine(_dataRoot, "backups");

            if (!Directory.Exists(directory))
            {
                return [];
            }

            return Directory.GetFiles(directory, "carpinteria_*.db")
                .Where(f => !Path.GetFileName(f).Contains("pre_restore", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e) => OpenFolder(_dataRoot);

    private void OnRestoreBackup(object sender, RoutedEventArgs e) =>
        OpenFolder(Path.Combine(_dataRoot, "backups"));

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogService.Error("StartupFailureWindow", $"No se pudo abrir «{path}»", ex);
            MessageBox.Show(
                $"No se pudo abrir la carpeta:\n\n{path}",
                "Metro Carpintería",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
