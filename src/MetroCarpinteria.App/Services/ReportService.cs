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

        var openCash = context.CashSessions.Any(s => s.ClosedAtUtc == null);
        var closedSessions = context.CashSessions.Count(s => s.ClosedAtUtc != null);
        var lastClosed = context.CashSessions
            .AsNoTracking()
            .Where(s => s.ClosedAtUtc != null)
            .OrderByDescending(s => s.ClosedAtUtc)
            .Select(s => new
            {
                s.ClosingExpectedAmount,
                s.ClosingCountedAmount,
                s.ClosedAtUtc
            })
            .FirstOrDefault();

        var openSession = context.CashSessions
            .Include(s => s.Movements)
            .FirstOrDefault(s => s.ClosedAtUtc == null);

        decimal? todayIncome = null;
        decimal? todayExpense = null;
        decimal? todayExpected = null;

        if (openSession is not null)
        {
            todayIncome = openSession.Movements.Where(m => m.Type == CashMovementType.Income).Sum(m => m.Amount);
            todayExpense = openSession.Movements.Where(m => m.Type == CashMovementType.Expense).Sum(m => m.Amount);
            todayExpected = openSession.OpeningAmount + todayIncome - todayExpense;
        }

        var projectsQuote = context.Projects.Count(p => !p.IsArchived && p.Status == ProjectStatus.Quote);
        var projectsActive = context.Projects.Count(p => !p.IsArchived && p.Status == ProjectStatus.InProgress);
        var projectsCompleted = context.Projects.Count(p => !p.IsArchived && p.Status == ProjectStatus.Completed);
        var projectsDelivered = context.Projects.Count(p => !p.IsArchived && p.Status == ProjectStatus.Delivered);
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
                    openCash,
                    closedSessions,
                    lastClosed?.ClosingExpectedAmount,
                    lastClosed?.ClosingCountedAmount,
                    lastClosed?.ClosedAtUtc,
                    todayIncome,
                    todayExpense,
                    todayExpected,
                    culture)
            },
            new ReportSection
            {
                Title = "Proyectos y personal",
                Icon = "🪚",
                Metrics =
                [
                    new ReportMetric { Label = "En curso", Value = projectsActive.ToString() },
                    new ReportMetric { Label = "Presupuesto", Value = projectsQuote.ToString() },
                    new ReportMetric { Label = "Terminados", Value = projectsCompleted.ToString() },
                    new ReportMetric { Label = "Entregados", Value = projectsDelivered.ToString() },
                    new ReportMetric { Label = "Proyectos archivados", Value = projectsArchived.ToString() },
                    new ReportMetric { Label = "Personal activo", Value = employees.ToString() }
                ]
            }
        ];
    }

    private static List<ReportMetric> BuildCashMetrics(
        bool openCash,
        int closedSessions,
        decimal? lastExpected,
        decimal? lastCounted,
        DateTime? lastClosedUtc,
        decimal? todayIncome,
        decimal? todayExpense,
        decimal? todayExpected,
        System.Globalization.CultureInfo culture)
    {
        var metrics = new List<ReportMetric>
        {
            new() { Label = "Estado actual", Value = openCash ? "Abierta" : "Cerrada" },
            new() { Label = "Cajas cerradas", Value = closedSessions.ToString() }
        };

        if (openCash && todayExpected.HasValue)
        {
            metrics.Add(new ReportMetric
            {
                Label = "Ingresos (sesión abierta)",
                Value = (todayIncome ?? 0).ToString("C", culture)
            });
            metrics.Add(new ReportMetric
            {
                Label = "Egresos (sesión abierta)",
                Value = (todayExpense ?? 0).ToString("C", culture)
            });
            metrics.Add(new ReportMetric
            {
                Label = "Efectivo esperado",
                Value = todayExpected.Value.ToString("C", culture)
            });
        }

        if (lastClosedUtc.HasValue)
        {
            metrics.Add(new ReportMetric
            {
                Label = "Último cierre",
                Value = lastClosedUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", culture),
                Detail = $"Esperado {lastExpected?.ToString("C", culture) ?? "—"} · " +
                         $"Contado {lastCounted?.ToString("C", culture) ?? "—"}"
            });
        }

        return metrics;
    }
}
