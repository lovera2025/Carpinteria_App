using MetroCarpinteria.App.Data;
using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Models;
using Microsoft.EntityFrameworkCore;

namespace MetroCarpinteria.App.Services;

public sealed class CashRegisterService
{
    private readonly DatabaseService _databaseService;

    public CashRegisterService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public OpenCashSessionState? GetOpenSessionState()
    {
        using var context = _databaseService.CreateContext();
        var session = GetOpenSessionEntity(context);
        return session is null ? null : BuildOpenState(session);
    }

    public bool HasOpenSession()
    {
        using var context = _databaseService.CreateContext();
        return context.CashSessions.Any(s => s.ClosedAtUtc == null);
    }

    public CashSession OpenSession(decimal openingAmount, string? openingNotes)
    {
        if (openingAmount < 0)
        {
            throw new InvalidOperationException("El monto inicial no puede ser negativo.");
        }

        using var context = _databaseService.CreateContext();

        if (context.CashSessions.Any(s => s.ClosedAtUtc == null))
        {
            throw new InvalidOperationException("Ya hay una caja abierta. Cerrala antes de abrir otra.");
        }

        var session = new CashSession
        {
            OpeningAmount = openingAmount,
            OpeningNotes = string.IsNullOrWhiteSpace(openingNotes) ? null : openingNotes.Trim(),
            OpenedAtUtc = DateTime.UtcNow
        };

        context.CashSessions.Add(session);
        context.SaveChanges();
        return session;
    }

    public void RegisterMovement(CashMovementType type, decimal amount, string reason)
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
        using var transaction = context.Database.BeginTransaction();

        try
        {
            var session = GetOpenSessionEntity(context)
                ?? throw new InvalidOperationException("No hay una caja abierta.");

            context.CashMovements.Add(new CashMovement
            {
                CashSessionId = session.Id,
                Type = type,
                Amount = amount,
                Reason = reason.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            });

            context.SaveChanges();
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public CashSession CloseSession(decimal countedAmount, string? closingNotes)
    {
        if (countedAmount < 0)
        {
            throw new InvalidOperationException("El monto contado no puede ser negativo.");
        }

        using var context = _databaseService.CreateContext();
        using var transaction = context.Database.BeginTransaction();

        try
        {
            var session = context.CashSessions
                .Include(s => s.Movements)
                .FirstOrDefault(s => s.ClosedAtUtc == null)
                ?? throw new InvalidOperationException("No hay una caja abierta para cerrar.");

            var expected = CalculateExpectedBalance(session);
            session.ClosingExpectedAmount = expected;
            session.ClosingCountedAmount = countedAmount;
            session.Difference = countedAmount - expected;
            session.ClosingNotes = string.IsNullOrWhiteSpace(closingNotes) ? null : closingNotes.Trim();
            session.ClosedAtUtc = DateTime.UtcNow;

            context.SaveChanges();
            transaction.Commit();
            return session;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public IReadOnlyList<CashMovementListItem> GetCurrentMovements()
    {
        using var context = _databaseService.CreateContext();
        var session = GetOpenSessionEntity(context);
        if (session is null)
        {
            return [];
        }

        return GetMovementsForSession(context, session.Id);
    }

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

    private static CashSession? GetOpenSessionEntity(AppDbContext context)
    {
        return context.CashSessions
            .Include(s => s.Movements)
            .FirstOrDefault(s => s.ClosedAtUtc == null);
    }

    private static OpenCashSessionState BuildOpenState(CashSession session)
    {
        var income = session.Movements
            .Where(m => m.Type == CashMovementType.Income)
            .Sum(m => m.Amount);
        var expense = session.Movements
            .Where(m => m.Type == CashMovementType.Expense)
            .Sum(m => m.Amount);

        return new OpenCashSessionState
        {
            Id = session.Id,
            OpeningAmount = session.OpeningAmount,
            IncomeTotal = income,
            ExpenseTotal = expense,
            ExpectedBalance = session.OpeningAmount + income - expense,
            OpenedAtLocal = session.OpenedAtUtc.ToLocalTime(),
            OpeningNotes = session.OpeningNotes
        };
    }

    private static decimal CalculateExpectedBalance(CashSession session)
    {
        var income = session.Movements.Where(m => m.Type == CashMovementType.Income).Sum(m => m.Amount);
        var expense = session.Movements.Where(m => m.Type == CashMovementType.Expense).Sum(m => m.Amount);
        return session.OpeningAmount + income - expense;
    }

    private static List<CashMovementListItem> GetMovementsForSession(AppDbContext context, int sessionId)
    {
        return context.CashMovements
            .AsNoTracking()
            .Where(m => m.CashSessionId == sessionId)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => new CashMovementListItem
            {
                Id = m.Id,
                TypeLabel = m.Type == CashMovementType.Income ? "Ingreso" : "Egreso",
                Amount = m.Amount,
                Reason = m.Reason,
                CreatedAtLocal = m.CreatedAtUtc.ToLocalTime(),
                IsIncome = m.Type == CashMovementType.Income
            })
            .ToList();
    }
}
