using System.Windows.Threading;

namespace MetroCarpinteria.App.Helpers;

/// <summary>
/// Agrupa ráfagas de llamadas en una sola: cada <see cref="Run"/> reinicia la cuenta y
/// recién cuando pasa el retardo sin novedades se ejecuta lo último que se pidió.
/// </summary>
/// <remarks>
/// <para>
/// Los buscadores actualizan la lista con cada tecla. Tipear "mesada" son seis consultas
/// a SQLite, sincrónicas sobre el hilo de la interfaz, y las cinco primeras se descartan
/// apenas se escribe la letra siguiente. Con 250 ms queda una sola, y el retardo es corto
/// de más para notarse mientras se tipea.
/// </para>
/// <para>
/// El temporizador es de WPF, así que la acción corre en el hilo de la interfaz: los
/// ViewModels pueden tocar sus <c>ObservableCollection</c> sin marshalling.
/// </para>
/// </remarks>
public sealed class Debouncer
{
    /// <summary>
    /// Retardo de los debouncers que no piden uno propio.
    /// <para>
    /// Los tests lo ponen en cero: corren sin bucle de mensajes, así que un temporizador
    /// no llegaría a disparar nunca y cada aserción sobre una búsqueda quedaría esperando
    /// una lista que no se actualiza.
    /// </para>
    /// </summary>
    public static TimeSpan DefaultDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer? _timer;
    private Action? _pending;

    public Debouncer(TimeSpan? delay = null)
    {
        var interval = delay ?? DefaultDelay;

        // Con retardo cero no hay nada que agrupar y se ejecuta en el acto.
        if (interval <= TimeSpan.Zero)
        {
            return;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = interval };
        _timer.Tick += (_, _) => Flush();
    }

    /// <summary>Pide ejecutar <paramref name="action"/> cuando se calme la ráfaga.</summary>
    public void Run(Action action)
    {
        if (_timer is null)
        {
            action();
            return;
        }

        _pending = action;

        // Stop antes de Start: sin eso el temporizador sigue corriendo desde donde estaba
        // y la ráfaga se cortaría igual a los 250 ms del primer llamado.
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Ejecuta ya lo que estaba esperando. Sirve para no hacer esperar a un Enter.</summary>
    public void Flush()
    {
        _timer?.Stop();

        var pending = _pending;
        _pending = null;
        pending?.Invoke();
    }

    public void Cancel()
    {
        _timer?.Stop();
        _pending = null;
    }
}
