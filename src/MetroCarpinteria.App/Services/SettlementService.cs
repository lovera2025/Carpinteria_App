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
            items.Add(new SettlementProjectItem
            {
                Id = project.Id,
                Title = project.Title,
                ClientName = project.ClientName,
                Budget = project.Budget,
                UpdatedAtLocal = project.UpdatedAtUtc.ToLocalTime(),
                Breakdown = _quoteService.GetDetail(project.Id)?.Breakdown,
                Workers = GetWorkers(project.Id)
            });
        }

        return items;
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

        return lines
            .Select(l => new SettlementWorkerItem
            {
                ProjectId = projectId,
                LaborLineId = l.Id,
                EmployeeId = l.EmployeeId,
                Description = string.IsNullOrWhiteSpace(l.Description) ? "Operario" : l.Description,
                Days = l.Days,
                DailyRate = l.DailyRate,

                // Días × jornal se calcula en memoria y con los valores congelados de la
                // línea: son columnas TEXT, y multiplicarlas en SQL las pasaría por punto
                // flotante.
                Due = Math.Round(l.Days * l.DailyRate, 2, MidpointRounding.AwayFromZero),
                Paid = paid.TryGetValue(l.Id, out var amount) ? amount : 0m
            })
            .ToList();
    }

    /// <summary>
    /// Cuánto se le debe a cada persona, sumando todos los trabajos terminados.
    /// </summary>
    /// <remarks>
    /// Alimenta el aviso de a quién le debe. Junta por ficha de Personal cuando la hay, y
    /// por nombre cuando el operario se cargó suelto.
    /// </remarks>
    public IReadOnlyList<SettlementDebtItem> GetPendingByWorker()
    {
        var pending = GetFinished()
            .SelectMany(p => p.Workers)
            .Where(w => w.Pending > 0m)
            .ToList();

        return pending
            .GroupBy(w => w.EmployeeId.HasValue
                ? $"#{w.EmployeeId.Value}"
                : $"@{w.Description.Trim().ToLowerInvariant()}")
            .Select(g => new SettlementDebtItem
            {
                EmployeeId = g.First().EmployeeId,
                Description = g.First().Description,
                Pending = g.Sum(w => w.Pending),
                ProjectCount = g.Select(w => w.ProjectId).Distinct().Count()
            })
            .OrderByDescending(d => d.Pending)
            .ToList();
    }

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
