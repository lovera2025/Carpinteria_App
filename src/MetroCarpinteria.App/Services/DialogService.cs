using System.Windows;
using MetroCarpinteria.App.Helpers;

namespace MetroCarpinteria.App.Services;

public enum DialogKind
{
    Question,
    Info,
    Warning,
    Danger
}

/// <summary>Lo que hay que mostrar. La vista se encarga de cómo.</summary>
public sealed class DialogRequest : ObservableObject
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public string ConfirmText { get; init; } = "Aceptar";
    public string CancelText { get; init; } = "Cancelar";
    public bool ShowCancel { get; init; } = true;
    public DialogKind Kind { get; init; } = DialogKind.Question;

    public string Icon => Kind switch
    {
        DialogKind.Danger => "⚠",
        DialogKind.Warning => "⚠",
        DialogKind.Info => "ℹ",
        _ => "?"
    };

    internal TaskCompletionSource<bool> Completion { get; } = new();
}

/// <summary>
/// Confirmaciones y avisos con el aspecto de la aplicación.
/// <para>
/// Reemplaza los catorce <c>MessageBox.Show</c> repartidos por la app, que eran cuadros
/// grises de Windows con tipografía y botones del sistema: rompían visualmente con todo
/// lo demás justo en los momentos importantes, que son los de borrar y aprobar.
/// </para>
/// <para>
/// El diálogo se dibuja como una capa dentro de la ventana y no como una ventana aparte.
/// Una <c>Window</c> sin marco obliga a reimplementar el arrastre, la sombra, el
/// posicionamiento con varios monitores y el escalado por DPI; la capa lo resuelve todo
/// gratis. Lo que se pierde es poder sacar el diálogo fuera de la ventana, que acá no hace falta.
/// </para>
/// </summary>
public sealed class DialogService
{
    private DialogRequest? _current;

    /// <summary>Lo que hay que mostrar ahora, o null. La capa visual se ata a esto.</summary>
    public DialogRequest? Current
    {
        get => _current;
        private set
        {
            _current = value;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CurrentChanged;

    /// <summary>
    /// Lo prende la capa visual cuando está montada. Mientras sea falso, el servicio cae
    /// al cuadro de Windows en vez de esperar una respuesta que nadie va a dar.
    /// </summary>
    public bool HasHost { get; set; }

    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "Confirmar",
        string cancelText = "Cancelar",
        bool isDestructive = false)
    {
        var request = new DialogRequest
        {
            Title = title,
            Message = message,
            ConfirmText = confirmText,
            CancelText = cancelText,
            Kind = isDestructive ? DialogKind.Danger : DialogKind.Question
        };

        return ShowAsync(request);
    }

    public Task AlertAsync(string title, string message, DialogKind kind = DialogKind.Info)
    {
        var request = new DialogRequest
        {
            Title = title,
            Message = message,
            ConfirmText = "Entendido",
            ShowCancel = false,
            Kind = kind
        };

        return ShowAsync(request);
    }

    private Task<bool> ShowAsync(DialogRequest request)
    {
        // Sin capa visual montada, esperar la respuesta colgaría el hilo para siempre.
        // Pasa en los tests, que instancian pantallas sueltas sin la ventana principal.
        if (!HasHost)
        {
            return Task.FromResult(FallbackToMessageBox(request));
        }

        if (Current is not null)
        {
            // Dos diálogos a la vez no tienen sentido: el segundo taparía al primero y
            // el usuario respondería sin saber a qué pregunta.
            LogService.Warning("DialogService", $"Ya hay un diálogo abierto; se descartó «{request.Title}».");
            return Task.FromResult(false);
        }

        Current = request;
        return request.Completion.Task;
    }

    /// <summary>La respuesta del usuario. La llama la capa visual.</summary>
    public void Complete(bool confirmed)
    {
        var request = Current;
        Current = null;
        request?.Completion.TrySetResult(confirmed);
    }

    private static bool FallbackToMessageBox(DialogRequest request)
    {
        if (Application.Current is null)
        {
            // Ni siquiera hay aplicación: se responde que no, que es lo seguro para
            // una confirmación de borrado, y queda registrado.
            LogService.Warning("DialogService", $"Sin interfaz para preguntar «{request.Title}»; se asumió que no.");
            return false;
        }

        var result = MessageBox.Show(
            request.Message,
            request.Title,
            request.ShowCancel ? MessageBoxButton.YesNo : MessageBoxButton.OK,
            request.Kind is DialogKind.Danger or DialogKind.Warning
                ? MessageBoxImage.Warning
                : MessageBoxImage.Question);

        return result is MessageBoxResult.Yes or MessageBoxResult.OK;
    }
}
