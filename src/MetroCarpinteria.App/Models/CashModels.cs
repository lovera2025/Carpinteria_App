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

    /// <summary>
    /// Dónde está físicamente esa plata.
    /// </summary>
    /// <remarks>
    /// El medio de pago dice cómo entró; lo que el taller necesita saber es dónde está
    /// ahora. «Efectivo» y «Transferencia» son la misma plata en dos lugares distintos, y
    /// el que se puede contar con la mano es uno solo.
    /// </remarks>
    public string WhereDisplay => Method switch
    {
        PaymentMethod.Cash => "en el cajón",
        PaymentMethod.Transfer or PaymentMethod.Card => "en el banco",
        PaymentMethod.Check => "en cheques",
        _ => string.Empty
    };

    /// <summary>Salió más de lo que entró por este medio.</summary>
    /// <remarks>
    /// No es un error de la app: pasa cuando se paga en efectivo una plata que entró por
    /// el banco. Vale explicarlo, porque un número en rojo asusta.
    /// </remarks>
    public bool IsNegative => Balance < 0m;

    public string NegativeNote => IsNegative
        ? "salió más de lo que entró por acá"
        : string.Empty;
}

/// <summary>
/// El estado de la caja fuerte: cuánta plata hay y cómo se reparte por medio.
/// </summary>
/// <remarks>
/// No hay sesión ni arqueo: es el acumulado de todo lo que entró menos todo lo que salió,
/// desde siempre. El desglose por medio sirve para filtrar y para leer cada movimiento; el
/// saldo no se parte en «cajón» y «banco», porque el taller no hace esa división.
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

    /// <summary>
    /// Cuánto del saldo se movió en efectivo. No se muestra en ninguna pantalla —
    /// mostrarlo daba números negativos que en billetes son imposibles. Lo usan los tests
    /// para verificar que un cobro entra con su medio y no se suma al efectivo si fue
    /// transferencia.
    /// </summary>
    public decimal CashOnHand => ByMethod
        .Where(m => m.Method == PaymentMethod.Cash)
        .Sum(m => m.Balance);

    public static CashBalance Empty { get; } = new();
}

/// <summary>Una fila del desglose de dónde sale el saldo de la caja.</summary>
public sealed class CashOriginTotal
{
    public required string Label { get; init; }
    public decimal Amount { get; init; }
    public int Count { get; init; }

    public string AmountDisplay => AppCulture.Money(Amount);
}

/// <summary>
/// Una apertura de caja vieja que puede estar contada dos veces.
/// </summary>
/// <remarks>
/// Las cajas de antes no encadenaban saldo: cada apertura se tipeaba de cero. Si alguna
/// vez se tipeó ahí <b>lo que había quedado del día anterior</b>, esa plata ya está
/// representada por los movimientos de la caja anterior, y convertir la apertura en
/// ingreso la suma de nuevo. No se puede saber con certeza, pero sí se puede señalar:
/// coincide con lo que se contó al cerrar la caja previa.
/// </remarks>
public sealed class SuspiciousOpening
{
    public required int MovementId { get; init; }
    public decimal Amount { get; init; }
    public DateTime OpenedAtLocal { get; init; }
    public decimal PreviousCounted { get; init; }
    public DateTime PreviousClosedAtLocal { get; init; }

    public string AmountDisplay => AppCulture.Money(Amount);

    public string Explanation =>
        $"Apertura del {AppCulture.ShortDate(OpenedAtLocal)} por {AmountDisplay}: es lo mismo que " +
        $"se contó al cerrar la caja del {AppCulture.ShortDate(PreviousClosedAtLocal)}. " +
        "Si era la plata que venía del día anterior, está sumada dos veces.";
}

/// <summary>
/// El saldo de la caja abierto en de dónde sale, para que el taller pueda confirmarlo.
/// </summary>
/// <remarks>
/// Preguntar «¿está bien $48.300?» no se puede contestar. Con el número abierto por
/// origen sí: se reconoce lo propio, y lo que no se reconoce se puede señalar.
/// </remarks>
public sealed class CashConversionReview
{
    public decimal Balance { get; init; }
    public IReadOnlyList<CashOriginTotal> Origins { get; init; } = [];
    public IReadOnlyList<SuspiciousOpening> Suspicious { get; init; } = [];

    public string BalanceDisplay => AppCulture.Money(Balance);
    public bool HasSuspicious => Suspicious.Count > 0;

    public string SuspiciousSummary => Suspicious.Count switch
    {
        0 => string.Empty,
        1 => "1 apertura podría estar contada dos veces",
        _ => $"{Suspicious.Count} aperturas podrían estar contadas dos veces"
    };
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
