using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Models;

namespace MetroCarpinteria.App.Services;

/// <summary>Cómo se recorre una transición: con un botón del taller o con una acción del ciclo.</summary>
public enum StatusChangeKind
{
    /// <summary>
    /// La marca el taller con un botón de Proyectos —Iniciar trabajo, Marcar listo, Seguir
    /// en taller—. No mueve inventario: solo dice en qué anda el trabajo.
    /// </summary>
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
        new(ProjectStatus.Approved, ProjectStatus.InProgress, StatusChangeKind.Manual),
        new(ProjectStatus.InProgress, ProjectStatus.Completed, StatusChangeKind.Manual),

        // Un trabajo chico se aprueba, se hace y se entrega en el día: obligar a pasar por
        // «En taller» para poder marcarlo listo sería puro trámite.
        new(ProjectStatus.Approved, ProjectStatus.Completed, StatusChangeKind.Manual),

        // Y la marcha atrás de un paso, para corregir un clic equivocado.
        new(ProjectStatus.Completed, ProjectStatus.InProgress, StatusChangeKind.Manual),

        new(ProjectStatus.Quote, ProjectStatus.Approved, StatusChangeKind.Workflow,
            "«Aprobar» desde Presupuestos, que es lo que descuenta el stock de los materiales"),
        new(ProjectStatus.Quote, ProjectStatus.Rejected, StatusChangeKind.Workflow,
            "«Marcar como rechazado» desde Presupuestos"),
        new(ProjectStatus.Rejected, ProjectStatus.Quote, StatusChangeKind.Workflow,
            "«Reabrir» desde Presupuestos"),

        // Se puede cancelar mientras el material siga en su sitio: aprobado (nadie lo tocó)
        // o en taller (se está usando pero todavía se puede devolver).
        new(ProjectStatus.Approved, ProjectStatus.Quote, StatusChangeKind.Workflow,
            "«Cancelar el trabajo», que devuelve los materiales al inventario"),
        new(ProjectStatus.InProgress, ProjectStatus.Quote, StatusChangeKind.Workflow,
            "«Cancelar el trabajo», que devuelve los materiales al inventario")
    ];

    /// <summary>Si el taller puede llevar el trabajo de <paramref name="from"/> a <paramref name="to"/>.</summary>
    /// <remarks>
    /// Quedarse donde está siempre vale: guardar sin cambiar el estado no es una transición.
    /// La consultan los botones de Proyectos para saber cuáles mostrar y el servicio para
    /// rechazar lo que no corresponde.
    /// </remarks>
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
            t => t.From == from && t.To == to && t.Kind == StatusChangeKind.Workflow)
            // Puede no haber salto directo y sí un camino: de «Presupuesto» a «En taller»
            // se llega aprobando y después iniciando. Nombrar ese primer paso es lo útil;
            // decir sólo que no se puede deja al usuario probando a ciegas.
            ?? Transitions.FirstOrDefault(
                t => t.From == from
                    && t.Kind == StatusChangeKind.Workflow
                    && CanReachManually(t.To, to));

        return workflow is not null
            ? $"Para pasar de «{fromLabel}» a «{toLabel}» hay que usar {workflow.Operation}."
            : $"Un trabajo en «{fromLabel}» no puede pasar a «{toLabel}».";
    }

    /// <summary>Si desde <paramref name="from"/> se llega a <paramref name="to"/> con pasos del taller.</summary>
    private static bool CanReachManually(ProjectStatus from, ProjectStatus to)
    {
        var seen = new HashSet<ProjectStatus> { from };
        var pending = new Queue<ProjectStatus>([from]);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();

            if (current == to)
            {
                return true;
            }

            foreach (var step in Transitions.Where(
                t => t.From == current && t.Kind == StatusChangeKind.Manual))
            {
                if (seen.Add(step.To))
                {
                    pending.Enqueue(step.To);
                }
            }
        }

        return false;
    }
}
