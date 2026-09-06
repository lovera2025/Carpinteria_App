using MetroCarpinteria.App.Data;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Models;
using Microsoft.EntityFrameworkCore;

namespace MetroCarpinteria.App.Services;

/// <summary>
/// La liquidación de los trabajos terminados: qué le toca a cada operario y cuánto cobró.
/// </summary>
/// <remarks>
/// <para>
/// El cálculo no es nuevo: <see cref="BudgetCalculatorService"/> ya repartía la mano de obra
/// persona por persona. Lo que faltaba era poder pagarla.
/// </para>
/// <para>
/// <b>El registro del pago es el movimiento de caja</b>, no un campo en la asignación. Con
/// el proyecto, el empleado y la línea de mano de obra anotados en el movimiento alcanza
/// para saber quién cobró qué, permite pagar en varias veces, y no hay dos números que
/// puedan discrepar. Por eso <c>ProjectAssignment.IsPaid</c> no se toca desde acá: lo que
/// él ya marcó a mano queda como está, sin registro retroactivo.
/// </para>
/// <para>
/// <b>Pero el tilde viejo se lee para avisar.</b> Un jornal que él marcó a mano antes de
/// que existiera la liquidación no dejó movimiento, así que acá figura pendiente y podría
/// pagarse dos veces. La cuenta no lo mira —un booleano que nunca movió plata no puede
/// decidir cuánto se debe—, pero la fila lo dice.
/// </para>
/// </remarks>
public sealed class SettlementService
{
    private readonly DatabaseService _databaseService;
    private readonly QuoteService _quoteService;
    private readonly CashRegisterService _cashRegisterService;

    public SettlementService(
        DatabaseService databaseService,
        QuoteService quoteService,
        CashRegisterService cashRegisterService)
    {
        _databaseService = databaseService;
        _quoteService = quoteService;
        _cashRegisterService = cashRegisterService;
    }

    /// <summary>
    /// Los trabajos terminados y sin archivar, del más reciente al más viejo.
    /// </summary>
    public IReadOnlyList<SettlementProjectItem> GetFinished()
    {
        using var context = _databaseService.CreateContext();

        var projects = context.Projects
            .AsNoTracking()
            .Where(p => !p.IsArchived && p.Status == ProjectStatus.Completed)
            .OrderByDescending(p => p.UpdatedAtUtc)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.ClientName,
                p.Budget,
                p.UpdatedAtUtc
            })
            .ToList();

        var items = new List<SettlementProjectItem>(projects.Count);

        foreach (var project in projects)
        {
            var breakdown = _quoteService.GetDetail(project.Id)?.Breakdown;
            var materials = GetMaterialSpend(project.Id);

            items.Add(new SettlementProjectItem
            {
                Id = project.Id,
                Title = project.Title,
                ClientName = project.ClientName,
                Budget = project.Budget,
                UpdatedAtLocal = project.UpdatedAtUtc.ToLocalTime(),
                Breakdown = breakdown,
                QuotedMaterials = breakdown?.MaterialsCost ?? 0m,
                SpentMaterials = materials.Spent,
                BilledExtras = materials.Billed,
                Workers = GetWorkers(project.Id)
            });
        }

        return items;
    }

    /// <summary>
    /// Lo que salió del inventario para un trabajo, y cuánto de eso se le cobró al cliente.
    /// </summary>
    /// <remarks>
    /// Se valúa con el costo congelado de cada material, no con el precio de hoy: lo que
    /// costó un mueble de agosto no puede cambiar en octubre porque subió la melamina. Los
    /// materiales sin costo cargado no suman nada — es «no sé cuánto costaba», no cero, y
    /// hacerlos valer cero mentiría hacia abajo.
    /// </remarks>
    private (decimal Spent, decimal Billed) GetMaterialSpend(int projectId)
    {
        using var context = _databaseService.CreateContext();

        // AsEnumerable antes de multiplicar: las cantidades y los costos son TEXT, y
        // cualquier cuenta que quede del lado de SQL se resuelve como texto.
        var rows = context.ProjectMaterials
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .Select(m => new { m.Quantity, m.UnitCost, m.BilledAmount })
            .AsEnumerable()
            .ToList();

        return (
            rows.Sum(m => m.UnitCost is null ? 0m : m.Quantity * m.UnitCost.Value),
            rows.Sum(m => m.BilledAmount ?? 0m));
    }

    /// <summary>Lo que le toca a cada operario de un trabajo, con lo que ya cobró.</summary>
    public IReadOnlyList<SettlementWorkerItem> GetWorkers(int projectId)
    {
        using var context = _databaseService.CreateContext();

        var lines = context.ProjectLaborLines
            .AsNoTracking()
            .Where(l => l.ProjectId == projectId)
            .OrderBy(l => l.SortOrder)
            .Select(l => new
            {
                l.Id,
                l.EmployeeId,
                l.Description,
                l.Days,
                l.DailyRate
            })
            .ToList();

        if (lines.Count == 0)
        {
            return [];
        }

        var paid = _cashRegisterService.GetPaidByLaborLine(projectId);
        var (markedIds, markedNames) = GetAssignmentsMarkedPaid(context, projectId);

        return lines
            .Select(l => new SettlementWorkerItem
            {
                ProjectId = projectId,
                LaborLineId = l.Id,
                EmployeeId = l.EmployeeId,
                Description = string.IsNullOrWhiteSpace(l.Description) ? "Operario" : l.Description,
                Days = l.Days,
                DailyRate = l.DailyRate,

                // Con ficha se cruza por legajo. Sin ficha se cruza por nombre, que es como
                // está el caso real: él asignó a la persona desde Proyectos y por otro lado
                // tecleó el operario en el presupuesto, sin elegirlo de la lista.
                WasMarkedPaidByHand = l.EmployeeId.HasValue
                    ? markedIds.Contains(l.EmployeeId.Value)
                    : !string.IsNullOrWhiteSpace(l.Description)
                      && markedNames.Contains(l.Description.Trim()),

                // Días × jornal se calcula en memoria y con los valores congelados de la
                // línea: son columnas TEXT, y multiplicarlas en SQL las pasaría por punto
                // flotante.
                Due = Math.Round(l.Days * l.DailyRate, 2, MidpointRounding.AwayFromZero),
                Paid = paid.TryGetValue(l.Id, out var amount) ? amount : 0m
            })
            .ToList();
    }

    /// <summary>
    /// Quiénes tienen el jornal de este trabajo marcado a mano con el tilde viejo, el que
    /// no movía plata: por legajo y también por nombre.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El dato no se borró: es su registro de lo que pagó antes de que la liquidación
    /// existiera. Se lee para avisar, nunca para cambiar un número.
    /// </para>
    /// <para>
    /// <b>Hace falta cruzar por nombre</b> porque así está el caso real: la asignación
    /// apunta a la ficha, pero el operario del presupuesto se tecleó suelto, sin elegirlo
    /// de la lista, y quedó sin legajo. Cruzando solo por legajo el aviso no aparecería
    /// justo donde hace falta.
    /// </para>
    /// <para>
    /// El nombre puede errarle —dos personas que se llaman igual, o el mismo escrito de
    /// dos formas—, y por eso no decide plata: lo peor que hace un falso positivo es
    /// pedirle que mire; lo que evita un falso negativo es pagar dos veces.
    /// </para>
    /// </remarks>
    private static (HashSet<int> Ids, HashSet<string> Names) GetAssignmentsMarkedPaid(
        AppDbContext context,
        int projectId)
    {
        var marked = context.ProjectAssignments
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.IsPaid)
            .Select(a => new { a.EmployeeId, a.Employee.FullName })
            .ToList();

        return (
            [.. marked.Select(m => m.EmployeeId)],
            marked
                .Select(m => m.FullName.Trim())
                .Where(n => n.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Cuánto se le debe a cada persona, sumando todos los trabajos terminados.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Alimenta el aviso de a quién le debe, acá y en Personal. Junta por ficha de Personal
    /// cuando la hay, y por nombre cuando el operario se cargó suelto.
    /// </para>
    /// <para>
    /// <b>Y junta las dos formas cuando son la misma persona.</b> En un trabajo la eligió
    /// de la lista y en otro tecleó el mismo nombre —que es como carga él—, así que
    /// agrupando por legajo a secas salía dos veces, con el mismo nombre en las dos filas y
    /// ninguna diciendo lo que realmente le debe. Un nombre suelto que coincide con el de
    /// alguien elegido de la lista se cuenta como esa persona.
    /// </para>
    /// <para>
    /// Dos fichas distintas que se llamen igual <b>no</b> se juntan: son dos personas
    /// dadas de alta, y el legajo es el dato que lo dice.
    /// </para>
    /// </remarks>
    public IReadOnlyList<SettlementDebtItem> GetPendingByWorker()
    {
        var pending = GetFinished()
            .SelectMany(p => p.Workers)
            .Where(w => w.Pending > 0m)
            .ToList();

        // El nombre de cada uno de los que sí salieron de la lista, para poder reconocerlo
        // cuando aparece tecleado en otro trabajo.
        var legajoPorNombre = pending
            .Where(w => w.EmployeeId.HasValue)
            .GroupBy(NameKey)
            .ToDictionary(g => g.Key, g => g.First().EmployeeId!.Value);

        return pending
            .GroupBy(w => w.EmployeeId.HasValue
                ? $"#{w.EmployeeId.Value}"
                : legajoPorNombre.TryGetValue(NameKey(w), out var legajo)
                    ? $"#{legajo}"
                    : $"@{NameKey(w)}")
            .Select(g => new SettlementDebtItem
            {
                EmployeeId = g.Select(w => w.EmployeeId).FirstOrDefault(id => id.HasValue),
                Description = g.First().Description,
                Pending = g.Sum(w => w.Pending),
                ProjectCount = g.Select(w => w.ProjectId).Distinct().Count()
            })
            .OrderByDescending(d => d.Pending)
            .ToList();
    }

    /// <summary>El nombre como se lo compara: sin espacios de más y sin distinguir mayúsculas.</summary>
    private static string NameKey(SettlementWorkerItem worker) =>
        worker.Description.Trim().ToLowerInvariant();

    /// <summary>
    /// Paga —entera o en parte— la mano de obra de una línea, asentando el egreso en Caja.
    /// </summary>
    /// <remarks>
    /// El importe lo pone él: la pantalla lo precarga con lo que falta, pero se puede
    /// editar. Pagar de más no se bloquea —puede ser un adelanto—, y pagar en varias veces
    /// sale solo, porque cada pago es un movimiento más.
    /// </remarks>
    public CashMovement Pay(
        int laborLineId,
        decimal amount,
        PaymentMethod method = PaymentMethod.Cash,
        string? note = null)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("El importe a pagar tiene que ser mayor a cero.");
        }

        using var context = _databaseService.CreateContext();

        var line = context.ProjectLaborLines
            .AsNoTracking()
            .Include(l => l.Project)
            .FirstOrDefault(l => l.Id == laborLineId)
            ?? throw new InvalidOperationException("No se encontró la línea de mano de obra.");

        if (line.Project.IsArchived)
        {
            throw new InvalidOperationException("El trabajo está archivado: no se puede registrar el pago.");
        }

        var who = string.IsNullOrWhiteSpace(line.Description) ? "Operario" : line.Description.Trim();
        var reason = $"Pago de mano de obra: {who} — {line.Project.Title}";

        if (!string.IsNullOrWhiteSpace(note))
        {
            reason += $" ({note.Trim()})";
        }

        return _cashRegisterService.RegisterMovement(
            CashMovementType.Expense,
            amount,
            reason,
            method,
            projectId: line.ProjectId,
            employeeId: line.EmployeeId,
            projectLaborLineId: line.Id,
            origin: CashMovementOrigin.WorkerPayment);
    }

    /// <summary>
    /// Cuánta mano de obra queda sin pagar en un trabajo. Es lo que mira el diálogo de
    /// archivar, para no esconder una deuda al sacar el trabajo de la lista.
    /// </summary>
    public decimal GetPendingForProject(int projectId) =>
        GetWorkers(projectId).Sum(w => w.Pending);
}
