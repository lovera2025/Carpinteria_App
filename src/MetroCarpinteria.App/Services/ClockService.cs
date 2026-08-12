using System.Windows.Threading;

namespace MetroCarpinteria.App.Services;

/// <summary>
/// Avisa cuando cambió el día, para que lo que depende de la fecha se vuelva a leer.
/// </summary>
/// <remarks>
/// <para>
/// La vigencia de un presupuesto («Vigente», «Por vencer», «Vencido») y la antigüedad
/// («hoy», «ayer») se derivan de la fecha de hoy. En el taller la app queda abierta días
/// enteros, así que un presupuesto que vencía hoy seguía figurando como vigente hasta que
/// alguien reiniciara: la pantalla que existe justamente para avisar qué hay que llamar
/// mostraba el estado de anteayer.
/// </para>
/// <para>
/// Dos disparadores porque ninguno alcanza solo: el temporizador cubre la app abierta y
/// visible, y el chequeo al activar la ventana cubre la máquina que estuvo suspendida
/// —mientras duerme, los temporizadores no corren—.
/// </para>
/// </remarks>
public sealed class ClockService
{
    /// <summary>
    /// Un minuto: el cambio de día no necesita más precisión que eso, y verificarlo es
    /// comparar dos fechas.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly Func<DateTime> _now;
    private DispatcherTimer? _timer;

    /// <param name="now">
    /// Sustituible para poder probar el cambio de día sin esperar a medianoche.
    /// </param>
    public ClockService(Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.Now);
        Today = _now().Date;
    }

    /// <summary>El día que la interfaz está mostrando ahora mismo.</summary>
    public DateTime Today { get; private set; }

    public event EventHandler? DayChanged;

    /// <summary>
    /// Arranca el sondeo. No tira si no hay bucle de mensajes —los tests instancian los
    /// servicios sueltos—: sin temporizador el chequeo manual sigue funcionando igual.
    /// </summary>
    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        try
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
            _timer.Tick += (_, _) => CheckForDayChange();
            _timer.Start();
        }
        catch (Exception ex)
        {
            _timer = null;
            LogService.Warning("ClockService", $"No se pudo iniciar el reloj: {ex.Message}");
        }
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>
    /// Compara contra la fecha real y avisa si cambió. La llama el temporizador y también
    /// la ventana principal al recuperar el foco.
    /// </summary>
    /// <returns><c>true</c> si el día cambió.</returns>
    public bool CheckForDayChange()
    {
        var today = _now().Date;

        if (today == Today)
        {
            return false;
        }

        Today = today;
        LogService.Info("ClockService", $"Cambio de día: {today:yyyy-MM-dd}");
        DayChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
