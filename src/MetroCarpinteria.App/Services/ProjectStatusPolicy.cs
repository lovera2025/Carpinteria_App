using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Models;

namespace MetroCarpinteria.App.Services;

/// <summary>Cómo se recorre una transición: eligiéndola en el formulario o con una acción.</summary>
public enum StatusChangeKind
{
    /// <summary>Se puede elegir directamente en el desplegable de estado de Proyectos.</summary>
    Manual,

    /// <summary>
    /// Solo la hace la acción que la acompaña, porque además de cambiar el estado mueve
    /// datos: aprobar descuenta inventario y cancelar lo devuelve.
    /// </summary>
    Workflow
}

/// <summary>
/// La única tabla de transiciones de estado de un proyecto. La consultan Proyectos
/// (para el formulario) y Presupuestos (para su ciclo de vida).
/// </summary>
/// <remarks>
/// <para>
/// Está en un solo lugar porque el ciclo se podía saltear: el desplegable de Proyectos
/// ofrecía los cinco estados siempre, sin importar en cuál estaba el trabajo. Pasar un
/// presupuesto a «En curso» desde ahí lo daba por aprobado <b>sin descontar un solo
/// material</b>, y devolver a «Presupuesto» un trabajo ya aprobado dejaba el stock
/// descontado para siempre, porque nada volvía a sumarlo.
/// </para>
/// <para>
/// Las transiciones <see cref="StatusChangeKind.Workflow"/> son justamente las que mueven
/// inventario. Que estén acá y no sueltas en cada pantalla es lo que garantiza que el
/// stock y el estado no se puedan separar.
/// </para>
/// </remarks>
public static class ProjectStatusPolicy
{
    private sealed record Transition(
        ProjectStatus From,
        ProjectStatus To,
        StatusChangeKind Kind,
        string? Operation = null);

    private static readonly IReadOnlyList<Transition> Transitions =
    [
        // Avance normal del trabajo: son las que el taller marca a mano.
        new(ProjectStatus.InProgress, ProjectStatus.Completed, StatusChangeKind.Manual),
        new(ProjectStatus.Completed, ProjectStatus.Delivered, StatusChangeKind.Manual),

        // Y su marcha atrás de un paso, para corregir un clic equivocado.
        new(ProjectStatus.Completed, ProjectStatus.InProgress, StatusChangeKind.Manual),
        new(ProjectStatus.Delivered, ProjectStatus.Completed, StatusChangeKind.Manual),

        new(ProjectStatus.Quote, ProjectStatus.InProgress, StatusChangeKind.Workflow,
            "«Aprobar» desde Presupuestos, que es lo que descuenta el stock de los materiales"),
        new(ProjectStatus.Quote, ProjectStatus.Rejected, StatusChangeKind.Workflow,
            "«Marcar como rechazado» desde Presupuestos"),
        new(ProjectStatus.Rejected, ProjectStatus.Quote, StatusChangeKind.Workflow,
            "«Reabrir» desde Presupuestos"),
        new(ProjectStatus.InProgress, ProjectStatus.Quote, StatusChangeKind.Workflow,
            "«Cancelar el trabajo», que devuelve los materiales al inventario")
    ];

    /// <summary>Estados que se pueden elegir en el formulario estando en <paramref name="from"/>.</summary>
    /// <remarks>Incluye el actual: no cambiar el estado siempre es válido.</remarks>
    public static IReadOnlyList<ProjectStatus> GetManualTargets(ProjectStatus from) =>
    [
        from,
        .. Transitions
            .Where(t => t.From == from && t.Kind == StatusChangeKind.Manual)
            .Select(t => t.To)
    ];

    /// <summary>Las opciones del desplegable de estado, ya filtradas y etiquetadas.</summary>
    public static IReadOnlyList<ProjectStatusOption> GetManualOptions(ProjectStatus from) =>
        GetManualTargets(from)
            .Select(status => new ProjectStatusOption
            {
                Status = status,
                Label = ProjectStatusHelper.GetLabel(status)
            })
            .ToList();

    public static bool CanChangeManually(ProjectStatus from, ProjectStatus to) =>
        from == to || Transitions.Any(t => t.From == from && t.To == to && t.Kind == StatusChangeKind.Manual);

    /// <summary>
    /// Corta un cambio de estado hecho a mano que no corresponda. Lo usa el servicio, no
    /// la pantalla: el formulario ya solo ofrece los válidos, pero el que garantiza es éste.
    /// </summary>
    public static void RequireManual(ProjectStatus from, ProjectStatus to)
    {
        if (CanChangeManually(from, to))
        {
            return;
        }

        throw new InvalidOperationException(Explain(from, to));
    }

    /// <summary>Corta una transición del ciclo de vida (aprobar, rechazar, reabrir, cancelar).</summary>
    public static void RequireWorkflow(ProjectStatus from, ProjectStatus to)
    {
        if (from == to || Transitions.Any(t => t.From == from && t.To == to))
        {
            return;
        }

        throw new InvalidOperationException(Explain(from, to));
    }

    /// <summary>
    /// Por qué no se puede, en castellano y nombrando la acción que sí lo hace. Un botón
    /// deshabilitado sin explicación deja al usuario probando combinaciones a ciegas.
    /// </summary>
    public static string Explain(ProjectStatus from, ProjectStatus to)
    {
        var fromLabel = ProjectStatusHelper.GetLabel(from);
        var toLabel = ProjectStatusHelper.GetLabel(to);

        var workflow = Transitions.FirstOrDefault(
            t => t.From == from && t.To == to && t.Kind == StatusChangeKind.Workflow);

        return workflow is not null
            ? $"Para pasar de «{fromLabel}» a «{toLabel}» hay que usar {workflow.Operation}."
            : $"Un trabajo en «{fromLabel}» no puede pasar a «{toLabel}».";
    }
}
