using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.App.ViewModels;

/// <summary>
/// Base de las pantallas, con el manejo de errores de carga.
/// <para>
/// Los métodos <c>Load()</c> se llaman desde el evento <c>Loaded</c> de cada vista y desde
/// el cambio de sección, y ninguno estaba protegido. Una base bloqueada por otra instancia
/// de la app, o un archivo corrupto, hacía que navegar a una sección tirara la excepción
/// hasta el manejador global: aparecía un cartel genérico y la sección quedaba vacía, sin
/// explicar nada ni ofrecer reintentar.
/// </para>
/// </summary>
public abstract class ViewModelBase : ValidatableObject
{
    private string _loadError = string.Empty;
    private string _busyMessage = string.Empty;

    /// <summary>
    /// Qué está corriendo ahora mismo. Vacío cuando no hay nada en curso.
    /// </summary>
    /// <remarks>
    /// Solo lo usan las tres operaciones con latencia de verdad —copiar la base, restaurar
    /// un respaldo y armar los reportes—. El resto responde en microsegundos: taparlas con
    /// un cartel de "cargando" que parpadea es peor que no mostrar nada.
    /// </remarks>
    public string BusyMessage
    {
        get => _busyMessage;
        private set
        {
            if (SetProperty(ref _busyMessage, value))
            {
                OnPropertyChanged(nameof(IsBusy));
            }
        }
    }

    public bool IsBusy => !string.IsNullOrEmpty(_busyMessage);

    /// <summary>Qué salió mal al cargar. Vacío cuando la última carga anduvo bien.</summary>
    public string LoadError
    {
        get => _loadError;
        private set
        {
            if (SetProperty(ref _loadError, value))
            {
                OnPropertyChanged(nameof(HasLoadError));
            }
        }
    }

    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);

    /// <summary>
    /// Corre una carga dejando la pantalla en un estado explicable si falla.
    /// </summary>
    /// <param name="context">De dónde viene, para poder ubicarlo en el log.</param>
    protected void SafeLoad(Action work, string context)
    {
        try
        {
            work();
            LoadError = string.Empty;
        }
        catch (Exception ex)
        {
            ReportLoadFailure(ex, context);
        }
    }

    /// <summary>
    /// Deja la pantalla explicando una carga que falló. Para las cargas asincrónicas, que
    /// no pueden envolverse en <see cref="SafeLoad"/>.
    /// </summary>
    protected void ReportLoadFailure(Exception exception, string context)
    {
        LogService.Error(context, "No se pudieron cargar los datos", exception);
        LoadError = BuildMessage(exception);
    }

    /// <summary>
    /// Corre en segundo plano algo que tarda, mostrando <paramref name="message"/> mientras.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La decisión de fondo es no volver los servicios asincrónicos: SQLite local sobre SSD
    /// responde en microsegundos y convertir quince servicios traería deadlocks y problemas
    /// de reentrada por muy poca ganancia. Se saca del hilo de la interfaz solo lo que de
    /// verdad tarda: copiar un archivo de varios megas y las consultas agregadas de Reportes.
    /// </para>
    /// <para>
    /// Al volver del <c>await</c> se sigue en el hilo de la interfaz, así que quien llama
    /// puede tocar sus colecciones sin marshalling.
    /// </para>
    /// </remarks>
    protected async Task<T> RunBusyAsync<T>(Func<T> work, string message)
    {
        BusyMessage = message;

        try
        {
            return await Task.Run(work);
        }
        finally
        {
            BusyMessage = string.Empty;
        }
    }

    protected async Task RunBusyAsync(Action work, string message)
    {
        BusyMessage = message;

        try
        {
            await Task.Run(work);
        }
        finally
        {
            BusyMessage = string.Empty;
        }
    }

    /// <summary>
    /// Traduce las fallas conocidas a algo que alguien en el taller pueda accionar.
    /// </summary>
    private static string BuildMessage(Exception exception)
    {
        var detail = exception.Message;

        if (detail.Contains("database is locked", StringComparison.OrdinalIgnoreCase))
        {
            return "La base de datos está ocupada. Puede haber otra ventana de la app abierta. " +
                   "Cerrala y probá de nuevo con el botón Actualizar.";
        }

        if (exception is UnauthorizedAccessException)
        {
            return "Windows no dio permiso para leer la carpeta de datos. " +
                   "Revisá los permisos en Configuración → Ubicación de datos.";
        }

        return $"No se pudieron cargar los datos: {detail}";
    }
}
