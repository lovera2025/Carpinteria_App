using System.Windows.Input;

namespace MetroCarpinteria.App.Helpers;

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private readonly bool _observeRequery;

    private EventHandler? _canExecuteChanged;

    /// <param name="observeRequery">
    /// Engancharse a <see cref="CommandManager.RequerySuggested"/>, que dispara con cada
    /// tecla y cada clic. Cómodo, pero convierte al predicado en algo que corre decenas de
    /// veces por segundo: si consulta la base, son decenas de conexiones a SQLite
    /// sincrónicas sobre el hilo de la interfaz mientras el usuario tipea. Los comandos con
    /// un predicado caro lo apagan y avisan ellos con <see cref="RaiseCanExecuteChanged"/>.
    /// El default queda en <c>true</c> para no cambiar el resto.
    /// </param>
    public RelayCommand(
        Action<object?> execute,
        Func<object?, bool>? canExecute = null,
        bool observeRequery = true)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _observeRequery = observeRequery;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null, bool observeRequery = true)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute(), observeRequery)
    {
    }

    /// <remarks>
    /// Los controles de WPF se suscriben acá con un administrador de eventos débil, así que
    /// guardar los handlers no retiene las vistas que ya se descartaron.
    /// </remarks>
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

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>Avisa que el predicado cambió. Para los comandos que no observan el requery global.</summary>
    public void RaiseCanExecuteChanged() => _canExecuteChanged?.Invoke(this, EventArgs.Empty);
}
