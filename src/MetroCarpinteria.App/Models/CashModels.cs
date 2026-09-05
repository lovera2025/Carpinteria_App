using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;

namespace MetroCarpinteria.App.Models;

public sealed class CashSessionListItem
{
    public int Id { get; init; }
    public DateTime OpenedAtLocal { get; init; }
    public DateTime? ClosedAtLocal { get; init; }
    public decimal OpeningAmount { get; init; }
    public decimal? ClosingExpectedAmount { get; init; }
    public decimal? ClosingCountedAmount { get; init; }
    public decimal? Difference { get; init; }
    public bool IsOpen { get; init; }

    public string StatusLabel => IsOpen ? "Abierta" : "Cerrada";

    public string PeriodDisplay => ClosedAtLocal.HasValue
        ? $"{OpenedAtLocal:dd/MM/yyyy HH:mm} → {ClosedAtLocal:dd/MM/yyyy HH:mm}"
        : $"Abierta desde {OpenedAtLocal:dd/MM/yyyy HH:mm}";

    public string OpeningDisplay => AppCulture.Money(OpeningAmount);
    public string ExpectedDisplay => ClosingExpectedAmount.HasValue ? AppCulture.Money(ClosingExpectedAmount.Value) : "—";
    public string CountedDisplay => ClosingCountedAmount.HasValue ? AppCulture.Money(ClosingCountedAmount.Value) : "—";
    public string DifferenceDisplay => Difference.HasValue ? AppCulture.Money(Difference.Value) : "—";
}

/// <summary>Un renglón del historial de la caja fuerte.</summary>
public sealed class CashMovementListItem
{
    public int Id { get; init; }
    public decimal Amount { get; init; }
    public PaymentMethod Method { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTime CreatedAtLocal { get; init; }
    public bool IsIncome { get; init; }

    /// <summary>De qué trabajo salió o entró, si vino de uno.</summary>
    public int? ProjectId { get; init; }
    public string? ProjectTitle { get; init; }
    public string? ClientName { get; init; }
    public string? EmployeeName { get; init; }

    /// <summary>
    /// Cuánto quedaba en la caja después de este movimiento.
    /// </summary>
    /// <remarks>
    /// Sacado el arqueo, este es el número que permite recorrer el historial para atrás y
    /// encontrar dónde se torció una cuenta. Sin él, una lista larga es un montón de
    /// renglones sin ancla — que es exactamente por qué el taller no pudo explicar los
    /// 16.000 que le aparecieron.
    /// </remarks>
    public decimal RunningBalance { get; init; }

    public string TypeLabel => IsIncome ? "Ingreso" : "Egreso";
    public string MethodLabel => PaymentRules.GetMethodLabel(Method);
    public string AmountDisplay => (IsIncome ? "+ " : "- ") + AppCulture.Money(Amount);
    public string RunningBalanceDisplay => AppCulture.Money(RunningBalance);
    public string DateDisplay => AppCulture.ShortDate(CreatedAtLocal);

    /// <summary>De quién es esta plata, en una línea. Vacío en un movimiento suelto.</summary>
    public string OriginDisplay => (ProjectTitle, ClientName, EmployeeName) switch
    {
        (not null, not null, null) => $"{ProjectTitle} · {ClientName}",
        (not null, _, not null) => $"{ProjectTitle} · {EmployeeName}",
        (not null, null, null) => ProjectTitle!,
        (null, _, not null) => EmployeeName!,
        _ => string.Empty
    };

    public bool HasOrigin => OriginDisplay.Length > 0;
}

/// <summary>Cuánta plata entró y salió por un medio de pago.</summary>
public sealed class CashMethodTotal
{
    public required PaymentMethod Method { get; init; }
    public decimal Income { get; init; }
    public decimal Expense { get; init; }

    public decimal Balance => Income - Expense;

    public string MethodLabel => PaymentRules.GetMethodLabel(Method);
    public string IncomeDisplay => AppCulture.Money(Income);
    public string ExpenseDisplay => AppCulture.Money(Expense);
    public string BalanceDisplay => AppCulture.Money(Balance);
}

/// <summary>
/// El estado de la caja fuerte: cuánta plata hay y cómo se reparte por medio.
/// </summary>
/// <remarks>
/// No hay sesión ni arqueo: es el acumulado de todo lo que entró menos todo lo que salió,
/// desde siempre. El desglose por medio es lo que permite distinguir lo que está en el
/// cajón de lo que está en el banco sin mezclarlos en un solo total.
/// </remarks>
public sealed class CashBalance
{
    public decimal Income { get; init; }
    public decimal Expense { get; init; }
    public int MovementCount { get; init; }
    public IReadOnlyList<CashMethodTotal> ByMethod { get; init; } = [];

    public decimal Balance => Income - Expense;

    public string IncomeDisplay => AppCulture.Money(Income);
    public string ExpenseDisplay => AppCulture.Money(Expense);
    public string BalanceDisplay => AppCulture.Money(Balance);

    /// <summary>Lo que tiene que haber en billetes. El resto está en el banco.</summary>
    public decimal CashOnHand => ByMethod
        .Where(m => m.Method == PaymentMethod.Cash)
        .Sum(m => m.Balance);

    public string CashOnHandDisplay => AppCulture.Money(CashOnHand);

    public static CashBalance Empty { get; } = new();
}

/// <summary>Qué mostrar del historial de la caja.</summary>
public sealed class CashMovementFilter
{
    /// <summary>Null es «todos los medios».</summary>
    public PaymentMethod? Method { get; init; }

    public DateTime? FromLocal { get; init; }
    public DateTime? ToLocal { get; init; }

    public static CashMovementFilter All { get; } = new();
}
