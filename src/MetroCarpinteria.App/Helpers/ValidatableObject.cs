using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MetroCarpinteria.App.Helpers;

/// <summary>
/// Base para los ViewModels con formularios: guarda los errores por campo y avisa a WPF.
/// <para>
/// Con <see cref="INotifyDataErrorInfo"/> el error se dibuja <b>debajo del campo</b> que lo
/// causó y en el momento en que se tipea. Antes toda la validación era imperativa y llegaba
/// después de apretar Guardar, como un texto arriba de la pantalla que no decía a qué campo
/// correspondía: en un formulario de seis campos había que adivinar cuál corregir.
/// </para>
/// </summary>
public abstract class ValidatableObject : ObservableObject, INotifyDataErrorInfo
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public bool HasErrors => _errors.Count > 0;

    /// <summary>Todos los errores juntos, para explicar por qué Guardar está deshabilitado.</summary>
    public IReadOnlyList<string> AllErrors => _errors.Values.SelectMany(e => e).ToList();

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return AllErrors;
        }

        return _errors.TryGetValue(propertyName, out var errors) ? errors : Array.Empty<string>();
    }

    /// <summary>
    /// Corre las reglas de un campo y publica el resultado. Las reglas devuelven null
    /// cuando el valor está bien, así que el orden importa: gana el primer problema.
    /// </summary>
    protected void Validate(string propertyName, params Func<string?>[] rules)
    {
        var found = rules
            .Select(rule => rule())
            .Where(message => message is not null)
            .Select(message => message!)
            .ToList();

        if (found.Count == 0)
        {
            ClearErrors(propertyName);
            return;
        }

        _errors[propertyName] = found;
        RaiseErrorsChanged(propertyName);
    }

    protected void SetError(string propertyName, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            ClearErrors(propertyName);
            return;
        }

        _errors[propertyName] = [message];
        RaiseErrorsChanged(propertyName);
    }

    protected void ClearErrors(string propertyName)
    {
        if (_errors.Remove(propertyName))
        {
            RaiseErrorsChanged(propertyName);
        }
    }

    protected void ClearAllErrors()
    {
        if (_errors.Count == 0)
        {
            return;
        }

        var keys = _errors.Keys.ToList();
        _errors.Clear();

        foreach (var key in keys)
        {
            RaiseErrorsChanged(key);
        }
    }

    private void RaiseErrorsChanged([CallerMemberName] string? propertyName = null)
    {
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(AllErrors));
    }
}
