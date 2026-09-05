using MetroCarpinteria.App.Data;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Models;
using Microsoft.EntityFrameworkCore;

namespace MetroCarpinteria.App.Services;

/// <summary>
/// La caja fuerte del taller: un saldo que corre, sin sesiones ni arqueo.
/// </summary>
/// <remarks>
/// <para>
/// Entra y sale toda la plata, sea cual sea el medio, y cada movimiento anota de dónde
/// vino. El total se abre por medio para poder distinguir lo que está en el cajón de lo
/// que está en el banco.
/// </para>
/// <para>
/// <b>Nada se borra.</b> Un movimiento equivocado se corrige con otro que lo compensa, y
/// los dos quedan a la vista. El motivo sí se puede editar: es texto y no mueve ningún
/// saldo.
/// </para>
/// </remarks>
public sealed class CashRegisterService
{
    private readonly DatabaseService _databaseService;

    public CashRegisterService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    /// <summary>
    /// Cuánta plata hay en la caja fuerte, con el desglose por medio.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Se suma <b>en memoria</b> y no con un <c>SUM()</c> de SQL. <c>Amount</c> es una
    /// columna <c>TEXT</c> a propósito —ver <see cref="SchemaMigrator"/>— y SQLite, para
    /// sumarla, la convierte a punto flotante: devuelve un número parecido al correcto y
    /// nunca falla, que es la peor combinación posible para la plata de alguien. Es el
    /// mismo motivo por el que <see cref="PaymentService"/> y <see cref="ClientService"/>
    /// llaman a <c>AsEnumerable()</c> antes de sumar. No es algo a optimizar.
    /// </para>
    /// <para>
    /// Y se suma sobre <b>todas</b> las filas, nunca sobre las que la pantalla tenga
    /// cargadas: en cuanto la lista se recorte, el total dejaría de ser el total.
    /// </para>
    /// </remarks>
    public CashBalance GetBalance()
    {
        using var context = _databaseService.CreateContext();

        var rows = context.CashMovements
            .AsNoTracking()
            .Select(m => new { m.Type, m.Amount, m.Method })
            .AsEnumerable()
            .ToList();

        if (rows.Count == 0)
        {
            return CashBalance.Empty;
        }

        var byMethod = rows
            .GroupBy(m => m.Method)
            .Select(group => new CashMethodTotal
            {
                Method = group.Key,
                Income = group.Where(m => m.Type == CashMovementType.Income).Sum(m => m.Amount),
                Expense = group.Where(m => m.Type == CashMovementType.Expense).Sum(m => m.Amount)
            })
            .OrderByDescending(m => m.Balance)
            .ToList();

        return new CashBalance
        {
            Income = rows.Where(m => m.Type == CashMovementType.Income).Sum(m => m.Amount),
            Expense = rows.Where(m => m.Type == CashMovementType.Expense).Sum(m => m.Amount),
            MovementCount = rows.Count,
            ByMethod = byMethod
        };
    }

    /// <summary>
    /// El historial de la caja, con el saldo que quedaba después de cada movimiento.
    /// </summary>
    /// <remarks>
    /// El saldo acumulado se calcula sobre <b>todos</b> los movimientos en orden, y recién
    /// después se aplica el filtro. Calcularlo sobre lo filtrado daría una columna que
    /// parece un saldo y no lo es: mirando solo las transferencias, el «saldo» ignoraría
    /// todo el efectivo.
    /// </remarks>
    public IReadOnlyList<CashMovementListItem> GetMovements(
        CashMovementFilter? filter = null,
        int limit = 500)
    {
        using var context = _databaseService.CreateContext();

        var rows = context.CashMovements
            .AsNoTracking()
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.Type,
                m.Amount,
                m.Method,
                m.Reason,
                m.CreatedAtUtc,
                m.ProjectId,
                ProjectTitle = m.Project != null ? m.Project.Title : null,
                ClientName = m.Project != null ? m.Project.ClientName : null,
                EmployeeName = m.Employee != null ? m.Employee.FullName : null
            })
            .AsEnumerable()
            .ToList();

        var running = 0m;
        var items = new List<CashMovementListItem>(rows.Count);

        foreach (var row in rows)
        {
            var isIncome = row.Type == CashMovementType.Income;
            running += isIncome ? row.Amount : -row.Amount;

            items.Add(new CashMovementListItem
            {
                Id = row.Id,
                Amount = row.Amount,
                Method = row.Method,
                Reason = row.Reason,
                CreatedAtLocal = row.CreatedAtUtc.ToLocalTime(),
                IsIncome = isIncome,
                ProjectId = row.ProjectId,
                ProjectTitle = row.ProjectTitle,
                ClientName = row.ClientName,
                EmployeeName = row.EmployeeName,
                RunningBalance = running
            });
        }

        IEnumerable<CashMovementListItem> visible = items;

        if (filter is not null)
        {
            if (filter.Method.HasValue)
            {
                visible = visible.Where(m => m.Method == filter.Method.Value);
            }

            if (filter.FromLocal.HasValue)
            {
                var from = filter.FromLocal.Value.Date;
                visible = visible.Where(m => m.CreatedAtLocal >= from);
            }

            if (filter.ToLocal.HasValue)
            {
                // Hasta el final del día elegido: si no, filtrar «hasta hoy» escondía lo
                // que se cargó hoy mismo.
                var to = filter.ToLocal.Value.Date.AddDays(1);
                visible = visible.Where(m => m.CreatedAtLocal < to);
            }
        }

        // Lo último arriba, que es como se mira una caja.
        return visible.Reverse().Take(limit).ToList();
    }

    /// <summary>
    /// Asienta plata que entra o sale.
    /// </summary>
    /// <param name="projectId">De qué trabajo, si viene de uno.</param>
    /// <param name="employeeId">A quién se le pagó, si tiene ficha en Personal.</param>
    /// <param name="projectLaborLineId">Qué línea de mano de obra se está pagando.</param>
    public CashMovement RegisterMovement(
        CashMovementType type,
        decimal amount,
        string reason,
        PaymentMethod method = PaymentMethod.Cash,
        int? projectId = null,
        int? employeeId = null,
        int? projectLaborLineId = null)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("El monto debe ser mayor a cero.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Indicá un motivo para el movimiento.");
        }

        using var context = _databaseService.CreateContext();

        var movement = new CashMovement
        {
            Type = type,
            Amount = amount,
            Method = method,
            Reason = reason.Trim(),
            ProjectId = projectId,
            EmployeeId = employeeId,
            ProjectLaborLineId = projectLaborLineId,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.CashMovements.Add(movement);
        context.SaveChanges();
        return movement;
    }

    /// <summary>
    /// Corrige el importe de un movimiento asentando la diferencia.
    /// </summary>
    /// <remarks>
    /// No edita el importe original ni lo borra: asienta un movimiento que lleva el saldo
    /// al número correcto y explica por qué. Los dos quedan a la vista. Editar en el lugar
    /// dejaría el saldo bien y la historia muda.
    /// </remarks>
    public void CorrectAmount(int movementId, decimal correctAmount, string reason)
    {
        if (correctAmount < 0)
        {
            throw new InvalidOperationException("El importe corregido no puede ser negativo.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Indicá por qué se corrige el movimiento.");
        }

        using var context = _databaseService.CreateContext();

        var original = context.CashMovements.FirstOrDefault(m => m.Id == movementId)
            ?? throw new InvalidOperationException("Movimiento no encontrado.");

        var difference = correctAmount - original.Amount;

        if (difference == 0m)
        {
            throw new InvalidOperationException("El importe corregido es el mismo que ya estaba.");
        }

        // Si el original era un ingreso y ahora entra menos, el ajuste es una salida; y al
        // revés. Con un egreso, todo invertido.
        var isIncome = original.Type == CashMovementType.Income
            ? difference > 0m
            : difference < 0m;

        context.CashMovements.Add(new CashMovement
        {
            Type = isIncome ? CashMovementType.Income : CashMovementType.Expense,
            Amount = Math.Abs(difference),
            Method = original.Method,
            ProjectId = original.ProjectId,
            EmployeeId = original.EmployeeId,
            ProjectLaborLineId = original.ProjectLaborLineId,
            Reason = $"Corrección de «{original.Reason}»: {reason.Trim()}",
            CreatedAtUtc = DateTime.UtcNow
        });

        context.SaveChanges();
    }

    /// <summary>
    /// Cambia el motivo de un movimiento. Es texto: no mueve ningún saldo.
    /// </summary>
    public void UpdateReason(int movementId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Indicá un motivo para el movimiento.");
        }

        using var context = _databaseService.CreateContext();

        var movement = context.CashMovements.FirstOrDefault(m => m.Id == movementId)
            ?? throw new InvalidOperationException("Movimiento no encontrado.");

        movement.Reason = reason.Trim();
        context.SaveChanges();
    }

    /// <summary>
    /// Cuánto se le pagó a cada línea de mano de obra de un trabajo.
    /// </summary>
    /// <remarks>
    /// Es lo que alimenta el «pagado / falta» de la liquidación. El pago no se guarda en
    /// la asignación: los movimientos <b>son</b> el registro, así que pagar en varias
    /// veces sale solo y no hay dos números que puedan discrepar.
    /// </remarks>
    public IReadOnlyDictionary<int, decimal> GetPaidByLaborLine(int projectId)
    {
        using var context = _databaseService.CreateContext();

        return context.CashMovements
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId
                && m.ProjectLaborLineId != null
                && m.Type == CashMovementType.Expense)
            .Select(m => new { LineId = m.ProjectLaborLineId!.Value, m.Amount })
            .AsEnumerable()
            .GroupBy(m => m.LineId)
            .ToDictionary(group => group.Key, group => group.Sum(m => m.Amount));
    }

    /// <summary>
    /// Las cajas que se abrían y cerraban antes de que la caja fuera una sola.
    /// </summary>
    /// <remarks>
    /// La tabla se conserva y se puede consultar: es de dónde salieron las aperturas y los
    /// ajustes de arqueo que la migración convirtió en movimientos. Ya no se escribe.
    /// </remarks>
    public IReadOnlyList<CashSessionListItem> GetSessionHistory(int limit = 50)
    {
        using var context = _databaseService.CreateContext();
        return context.CashSessions
            .AsNoTracking()
            .OrderByDescending(s => s.OpenedAtUtc)
            .Take(limit)
            .Select(s => new CashSessionListItem
            {
                Id = s.Id,
                OpenedAtLocal = s.OpenedAtUtc.ToLocalTime(),
                ClosedAtLocal = s.ClosedAtUtc.HasValue ? s.ClosedAtUtc.Value.ToLocalTime() : null,
                OpeningAmount = s.OpeningAmount,
                ClosingExpectedAmount = s.ClosingExpectedAmount,
                ClosingCountedAmount = s.ClosingCountedAmount,
                Difference = s.Difference,
                IsOpen = s.ClosedAtUtc == null
            })
            .ToList();
    }
}
