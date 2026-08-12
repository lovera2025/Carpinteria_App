using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using MetroCarpinteria.App.Models;

namespace MetroCarpinteria.App.Services;

/// <summary>
/// Avisos flotantes que se descartan solos.
/// <para>
/// Reemplaza la barra de estado que estaba duplicada, con el mismo bloque de XAML, en seis
/// pantallas. El problema de aquella no era la duplicación sino que <b>nunca se borraba</b>:
/// un "Producto creado." quedaba fijo en pantalla, y al rato era imposible saber si
/// correspondía a la acción de recién o a una de veinte minutos antes.
/// </para>
/// <para>
/// Regla para decidir dónde va un mensaje: <b>si confirma algo que ya terminó, es un aviso
/// flotante; si describe un estado que sigue siendo cierto, va fijo en la pantalla.</b>
/// Por eso "Falta el valor del jornal" no se convierte en aviso flotante: no es el
/// resultado de una acción, es una condición que dura hasta que alguien complete el campo.
/// </para>
/// </summary>
public sealed class NotificationService
{
    /// <summary>Con más de cuatro a la vez ya no se leen, se apilan y tapan la pantalla.</summary>
    private const int MaxVisible = 4;

    private readonly Dictionary<ToastItem, DispatcherTimer> _timers = [];

    public ObservableCollection<ToastItem> Items { get; } = [];

    public void Success(string message) => Show(ToastKind.Success, message);

    public void Info(string message) => Show(ToastKind.Info, message);

    public void Warning(string message) => Show(ToastKind.Warning, message);

    public void Error(string message, Exception? exception = null)
    {
        LogService.Error("Notificación", message, exception);
        Show(ToastKind.Error, message, exception?.ToString() ?? string.Empty);
    }

    public void Show(ToastKind kind, string message, string detail = "")
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => Show(kind, message, detail));
            return;
        }

        var item = new ToastItem { Kind = kind, Message = message, Detail = detail };

        Items.Add(item);

        while (Items.Count > MaxVisible)
        {
            Dismiss(Items[0]);
        }

        // Sin Application no hay hilo de interfaz con Dispatcher andando (el caso de los
        // tests): el aviso queda en la lista y se puede verificar, pero no se auto-descarta.
        if (dispatcher is null)
        {
            return;
        }

        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = item.Duration
        };

        timer.Tick += (_, _) => Dismiss(item);
        _timers[item] = timer;
        timer.Start();
    }

    public void Dismiss(ToastItem item)
    {
        if (_timers.Remove(item, out var timer))
        {
            timer.Stop();
        }

        Items.Remove(item);
    }

    public void Clear()
    {
        foreach (var timer in _timers.Values)
        {
            timer.Stop();
        }

        _timers.Clear();
        Items.Clear();
    }
}
