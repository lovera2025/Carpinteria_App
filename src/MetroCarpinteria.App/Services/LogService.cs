using System.Diagnostics;
using System.IO;
using System.Text;

namespace MetroCarpinteria.App.Services;

public enum LogLevel
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Registro a disco de lo que pasa en la app.
/// <para>
/// Antes de esto, después de un cierre inesperado no quedaba ningún rastro: los
/// manejadores globales mostraban un <c>MessageBox</c> con el mensaje y nada más.
/// Si el problema aparecía en el taller, no había forma de saber qué había fallado.
/// </para>
/// <para>
/// Es estático y resuelve la carpeta por su cuenta —sin pasar por <see cref="AppHost"/>—
/// porque el caso que más importa loguear es justamente que <c>AppHost.Initialize()</c>
/// explotó y no hay ningún servicio disponible.
/// </para>
/// </summary>
public static class LogService
{
    private const int RetentionDays = 14;

    private static readonly object Gate = new();
    private static string? _logDirectory;

    /// <summary>Carpeta donde quedan los archivos, para poder mostrársela al usuario.</summary>
    public static string? Directory => _logDirectory;

    /// <summary>Archivo del día. Null si todavía no se pudo preparar la carpeta.</summary>
    public static string? CurrentFile =>
        _logDirectory is null ? null : Path.Combine(_logDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

    /// <summary>
    /// Prepara la carpeta de logs. Se llama lo más temprano posible en el arranque.
    /// No tira nunca: sin log la app tiene que seguir abriendo igual.
    /// </summary>
    public static void Bootstrap(string? rootDirectory = null)
    {
        try
        {
            var root = rootDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MetroCarpinteria");

            var directory = Path.Combine(root, "logs");
            System.IO.Directory.CreateDirectory(directory);
            _logDirectory = directory;

            CleanupOldFiles(directory);
            Write(LogLevel.Info, "App", "Arranque");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"No se pudo preparar el log: {ex.Message}");
        }
    }

    public static void Info(string context, string message) => Write(LogLevel.Info, context, message);

    public static void Warning(string context, string message) => Write(LogLevel.Warning, context, message);

    public static void Error(string context, string message, Exception? exception = null) =>
        Write(LogLevel.Error, context, message, exception);

    /// <summary>
    /// Escribe una línea. Si el disco falla se va a <c>Debug</c> y sigue: un logger que
    /// tira excepciones es peor que no tener logger, porque rompe el flujo que quería medir.
    /// </summary>
    public static void Write(LogLevel level, string context, string message, Exception? exception = null)
    {
        var file = CurrentFile;
        if (file is null)
        {
            Debug.WriteLine($"[{level}] {context} → {message}");
            return;
        }

        var text = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(Tag(level)).Append("] ")
            .Append(context).Append(" → ").Append(message)
            .AppendLine();

        if (exception is not null)
        {
            text.Append("    ").AppendLine(exception.GetType().FullName);
            text.AppendLine(Indent(exception.ToString()));
        }

        try
        {
            lock (Gate)
            {
                File.AppendAllText(file, text.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"No se pudo escribir el log: {ex.Message}");
        }
    }

    private static string Tag(LogLevel level) => level switch
    {
        LogLevel.Error => "ERR",
        LogLevel.Warning => "ADV",
        _ => "INF"
    };

    private static string Indent(string text) =>
        string.Join(Environment.NewLine, text.Split('\n').Select(l => "    " + l.TrimEnd('\r')));

    private static void CleanupOldFiles(string directory)
    {
        try
        {
            var limit = DateTime.Now.AddDays(-RetentionDays);

            foreach (var file in System.IO.Directory.GetFiles(directory, "app-*.log"))
            {
                if (File.GetLastWriteTime(file) < limit)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"No se pudieron limpiar los logs viejos: {ex.Message}");
        }
    }
}
