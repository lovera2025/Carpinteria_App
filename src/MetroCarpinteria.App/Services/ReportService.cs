using MetroCarpinteria.App.Data;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Models;
using Microsoft.EntityFrameworkCore;

namespace MetroCarpinteria.App.Services;

public sealed class ReportService
{
    private readonly DatabaseService _databaseService;

    public ReportService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public IReadOnlyList<ReportSection> BuildSummary()
    {
        using var context = _databaseService.CreateContext();
        var culture = Helpers.AppCulture.Current;

        var activeProducts = context.Products.Count(p => !p.IsArchived);
        var archivedProducts = context.Products.Count(p => p.IsArchived);

        // Mismo criterio que Inventario y el badge del menú (StockRules), y por la misma
        // razón se evalúa en memoria: comparar decimales en SQL sobre columnas TEXT
        // ordena alfabéticamente y devuelve cuentas equivocadas.
        var activeStock = context.Products
            .AsNoTracking()
            .Where(p => !p.IsArchived)
            .Select(p => new { p.CurrentStock, p.MinimumStock })
            .AsEnumerable()
            .ToList();

        var lowStock = activeStock.Count(p => StockRules.IsLowOrOut(p.CurrentStock, p.MinimumStock));
        var outOfStock = activeStock.Count(p => StockRules.IsOut(p.CurrentStock));

        // Los importes se suman en memoria: la columna es TEXT y un SUM() de SQL la
        // pasaría por punto flotante. Ver CashRegisterService.GetBalance.
        var cashRows = context.CashMovements
            .AsNoTracking()
            .Select(m => new { m.Type, m.Amount })
            .AsEnumerable()
            .ToList();

        var cashIncome = cashRows.Where(m => m.Type == CashMovementType.Income).Sum(m => m.Amount);
        var cashExpense = cashRows.Where(m => m.Type == CashMovementType.Expense).Sum(m => m.Amount);

        var projectsQuote = context.Projects.Count(p => !p.IsArchived && p.Status == ProjectStatus.Quote);
        var projectsApproved = context.Projects.Count(p => !p.IsArchived && p.Status == ProjectStatus.Approved);
        var projectsActive = context.Projects.Count(p => !p.IsArchived && p.Status == ProjectStatus.InProgress);
        var projectsCompleted = context.Projects.Count(p => !p.IsArchived && p.Status == ProjectStatus.Completed);
        var projectsArchived = context.Projects.Count(p => p.IsArchived);
        var employees = context.Employees.Count(e => !e.IsArchived);

        return
        [
            new ReportSection
            {
                Title = "Inventario",
                Icon = "📦",
                Metrics =
                [
                    new ReportMetric { Label = "Productos activos", Value = activeProducts.ToString() },
                    new ReportMetric { Label = "Stock bajo", Value = lowStock.ToString() },
                    new ReportMetric { Label = "Sin stock", Value = outOfStock.ToString() },
                    new ReportMetric { Label = "Archivados", Value = archivedProducts.ToString() }
                ]
            },
            new ReportSection
            {
                Title = "Caja",
                Icon = "💰",
                Metrics = BuildCashMetrics(
                    cashIncome, cashExpense, cashRows.Count, culture)
            },
            new ReportSection
            {
                Title = "Proyectos y personal",
                Icon = "🪚",
                Metrics =
                [
                    new ReportMetric { Label = "En taller", Value = projectsActive.ToString() },
                    new ReportMetric { Label = "Presupuesto", Value = projectsQuote.ToString() },
                    new ReportMetric { Label = "Aprobados", Value = projectsApproved.ToString() },
                    new ReportMetric { Label = "Listos", Value = projectsCompleted.ToString() },
                    new ReportMetric { Label = "Proyectos archivados", Value = projectsArchived.ToString() },
                    new ReportMetric { Label = "Personal activo", Value = employees.ToString() }
                ]
            }
        ];
    }

    /// <summary>
    /// La plata del taller: cuánta hay, cuánta entró y cuánta salió.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Antes esto decía «Estado actual: Abierta/Cerrada» y contaba cajas cerradas: números
    /// que ya no significan nada y que la pantalla igual mostraría, siempre iguales.
    /// </para>
    /// <para>
    /// Tampoco se parte el saldo por medio de pago. La plata del taller es una sola y el
    /// medio se lee en cada movimiento, no en el total. Partirlo mostraba «En efectivo:
    /// -$ 3.500,00 — lo que tendría que haber en billetes»: un imposible físico, porque los
    /// gastos quedan en efectivo y los cobros entran con su medio real.
    /// </para>
    /// </remarks>
    private static List<ReportMetric> BuildCashMetrics(
        decimal income,
        decimal expense,
        int movements,
        System.Globalization.CultureInfo culture) =>
        [
            new ReportMetric
            {
                Label = "En la caja",
                Value = (income - expense).ToString("C", culture),
                Detail = movements == 1 ? "1 movimiento" : $"{movements} movimientos"
            },
            new ReportMetric { Label = "Entró", Value = income.ToString("C", culture) },
            new ReportMetric { Label = "Salió", Value = expense.ToString("C", culture) }
        ];
}
