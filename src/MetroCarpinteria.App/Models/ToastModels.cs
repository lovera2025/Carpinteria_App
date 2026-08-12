using MetroCarpinteria.App.Helpers;

namespace MetroCarpinteria.App.Models;

public enum ToastKind
{
    Success,
    Info,
    Warning,
    Error
}

/// <summary>
/// Un aviso flotante. Confirma algo que ya pasó; no es para estados que persisten.
/// </summary>
public sealed class ToastItem : ObservableObject
{
    public required ToastKind Kind { get; init; }

    public required string Message { get; init; }

    /// <summary>Detalle técnico de un error, para el botón de copiar. Vacío en el resto.</summary>
    public string Detail { get; init; } = string.Empty;

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public string Icon => Kind switch
    {
        ToastKind.Success => "✓",
        ToastKind.Warning => "⚠",
        ToastKind.Error => "✕",
        _ => "ℹ"
    };

    /// <summary>
    /// Cuánto queda en pantalla. Un error dura más porque suele traer algo que leer;
    /// una confirmación de "guardado" alcanza con que se vea de reojo.
    /// </summary>
    public TimeSpan Duration => Kind switch
    {
        ToastKind.Error => TimeSpan.FromSeconds(9),
        ToastKind.Warning => TimeSpan.FromSeconds(7),
        _ => TimeSpan.FromSeconds(4)
    };
}
