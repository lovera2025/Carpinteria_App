using System.Windows.Input;

namespace MetroCarpinteria.App.Helpers;

/// <summary>
/// Versión asincrónica de <see cref="RelayCommand"/>, para acciones que esperan a la red
/// (buscar y bajar actualizaciones). Mientras corre queda deshabilitado, así un doble clic
/// no dispara dos descargas.
/// </summary>
public class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly bool _observeRequery;

    private EventHandler? _canExecuteChanged;
    private bool _isRunning;

    /// <param name="observeRequery">
    /// Igual que en <see cref="RelayCommand"/>: apagarlo saca al predicado del barrido
    /// global que dispara con cada tecla. Los comandos que lo apagan avisan con
    /// <see cref="RaiseCanExecuteChanged"/>.
    /// </param>
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, bool observeRequery = true)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = _ => execute();
        _canExecute = canExecute;
        _observeRequery = observeRequery;
    }

    /// <summary>
    /// Variante con parámetro, para las acciones que operan sobre un elemento de una lista
    /// (quitar un material de un proyecto, sacar a alguien de una asignación).
    /// </summary>
    public AsyncRelayCommand(
        Func<object?, Task> execute,
        Func<bool>? canExecute = null,
        bool observeRequery = true)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _observeRequery = observeRequery;
    }

    public event EventHandler? CanExecuteChanged
    {
        add
        {
            if (_observeRequery)
            {
                CommandManager.RequerySuggested += value;
            }

            _canExecuteChanged += value;
        }
        remove
        {
            if (_observeRequery)
            {
                CommandManager.RequerySuggested -= value;
            }

            _canExecuteChanged -= value;
        }
    }

    public void RaiseCanExecuteChanged() => _canExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool IsRunning => _isRunning;

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        NotifyRunningChanged();

        try
        {
            await _execute(parameter);
        }
        catch (Exception ex)
        {
            // Un comando async es void por fuera: sin este catch, cualquier excepción que
            // se escape del método viajaría como excepción de tarea no observada y
            // terminaría cerrando el proceso en vez de mostrarse.
            Services.LogService.Error("AsyncRelayCommand", "Falló una acción", ex);

            if (Services.AppHost.IsReady)
            {
                Services.AppHost.NotificationService.Error(ex.Message, ex);
            }
        }
        finally
        {
            _isRunning = false;
            NotifyRunningChanged();
        }
    }

    /// <summary>
    /// Mientras corre, el comando queda deshabilitado para que un doble clic no dispare la
    /// acción dos veces. Los que no observan el barrido global necesitan el aviso directo:
    /// sin esto se quedaban grises hasta el próximo cambio de selección.
    /// </summary>
    private void NotifyRunningChanged()
    {
        if (_observeRequery)
        {
            CommandManager.InvalidateRequerySuggested();
        }
        else
        {
            RaiseCanExecuteChanged();
        }
    }
}
