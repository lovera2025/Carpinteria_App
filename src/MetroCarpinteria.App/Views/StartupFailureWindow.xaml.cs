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

        ShowBackupHint();
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
