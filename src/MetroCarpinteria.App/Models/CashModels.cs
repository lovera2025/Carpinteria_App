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

    public string OpeningDisplay => FormatMoney(OpeningAmount);
    public string ExpectedDisplay => ClosingExpectedAmount.HasValue ? FormatMoney(ClosingExpectedAmount.Value) : "—";
    public string CountedDisplay => ClosingCountedAmount.HasValue ? FormatMoney(ClosingCountedAmount.Value) : "—";
    public string DifferenceDisplay => Difference.HasValue ? FormatMoney(Difference.Value) : "—";

    private static string FormatMoney(decimal amount) =>
        amount.ToString("C", new System.Globalization.CultureInfo("es-AR"));
}

public sealed class CashMovementListItem
{
    public int Id { get; init; }
    public string TypeLabel { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTime CreatedAtLocal { get; init; }
    public bool IsIncome { get; init; }

    public string AmountDisplay => (IsIncome ? "+ " : "- ") +
        Amount.ToString("C", new System.Globalization.CultureInfo("es-AR"));
}

public sealed class OpenCashSessionState
{
    public int Id { get; init; }
    public decimal OpeningAmount { get; init; }
    public decimal IncomeTotal { get; init; }
    public decimal ExpenseTotal { get; init; }
    public decimal ExpectedBalance { get; init; }
    public DateTime OpenedAtLocal { get; init; }
    public string? OpeningNotes { get; init; }

    public string OpeningDisplay => OpeningAmount.ToString("C", new System.Globalization.CultureInfo("es-AR"));
    public string IncomeDisplay => IncomeTotal.ToString("C", new System.Globalization.CultureInfo("es-AR"));
    public string ExpenseDisplay => ExpenseTotal.ToString("C", new System.Globalization.CultureInfo("es-AR"));
    public string ExpectedDisplay => ExpectedBalance.ToString("C", new System.Globalization.CultureInfo("es-AR"));
}
