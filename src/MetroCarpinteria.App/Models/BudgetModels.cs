using MetroCarpinteria.App.Helpers;

namespace MetroCarpinteria.App.Models;

/// <summary>
/// Porcentajes de la fórmula de presupuesto. Se guardan en <c>settings.json</c> y el
/// usuario los edita desde la calculadora; nunca están fijos en el código.
/// </summary>
public sealed class BudgetRates
{
    public const decimal DefaultWaste = 16m;
    public const decimal DefaultToolWear = 9m;
    public const decimal DefaultOverhead = 50m;
    public const decimal DefaultProfit = 30m;

    /// <summary>Merma de materiales, como % del costo de materiales.</summary>
    public decimal WastePercent { get; set; } = DefaultWaste;

    /// <summary>Desgaste de herramientas, como % del costo de materiales.</summary>
    public decimal ToolWearPercent { get; set; } = DefaultToolWear;

    /// <summary>Gastos adicionales, como % de la mano de obra.</summary>
    public decimal OverheadPercent { get; set; } = DefaultOverhead;

    /// <summary>Ganancia, como % de la mano de obra.</summary>
    public decimal ProfitPercent { get; set; } = DefaultProfit;

    public static BudgetRates Defaults() => new();

    public BudgetRates Clone() => new()
    {
        WastePercent = WastePercent,
        ToolWearPercent = ToolWearPercent,
        OverheadPercent = OverheadPercent,
        ProfitPercent = ProfitPercent
    };
}

public sealed class BudgetInput
{
    public decimal MaterialsCost { get; init; }
    public decimal Days { get; init; }
    public decimal DailyRate { get; init; }
    public BudgetRates Rates { get; init; } = new();
}

public sealed class BudgetBreakdownLine
{
    public required string Label { get; init; }
    public required decimal Amount { get; init; }
    public string? Detail { get; init; }

    /// <summary>Marca la línea del precio final para que la vista la destaque.</summary>
    public bool IsTotal { get; init; }

    public string AmountDisplay => AppCulture.Money(Amount);
}

/// <summary>Resultado del cálculo, con cada concepto por separado.</summary>
public sealed class BudgetBreakdown
{
    private IReadOnlyList<BudgetBreakdownLine>? _lines;
    private IReadOnlyList<BudgetBreakdownLine>? _clientLines;

    public decimal MaterialsCost { get; init; }
    public decimal Waste { get; init; }
    public decimal ToolWear { get; init; }
    public decimal Labor { get; init; }
    public decimal Overhead { get; init; }
    public decimal Profit { get; init; }
    public decimal FinalPrice { get; init; }
    public decimal Days { get; init; }
    public decimal DailyRate { get; init; }
    public BudgetRates Rates { get; init; } = new();

    public string FinalPriceDisplay => AppCulture.Money(FinalPrice);

    /// <summary>Desglose completo. Uso interno del taller: incluye ganancia y merma.</summary>
    public IReadOnlyList<BudgetBreakdownLine> Lines => _lines ??=
    [
        new() { Label = "Materiales", Amount = MaterialsCost },
        new()
        {
            Label = "Desperdicio",
            Amount = Waste,
            Detail = AppCulture.Percent(Rates.WastePercent) + " de materiales"
        },
        new()
        {
            Label = "Desgaste de herramientas",
            Amount = ToolWear,
            Detail = AppCulture.Percent(Rates.ToolWearPercent) + " de materiales"
        },
        new()
        {
            Label = "Mano de obra",
            Amount = Labor,
            Detail = $"{Days.ToString("0.##", AppCulture.Current)} × {AppCulture.Money(DailyRate)} por día"
        },
        new()
        {
            Label = "Gastos adicionales",
            Amount = Overhead,
            Detail = AppCulture.Percent(Rates.OverheadPercent) + " de mano de obra"
        },
        new()
        {
            Label = "Ganancia",
            Amount = Profit,
            Detail = AppCulture.Percent(Rates.ProfitPercent) + " de mano de obra"
        },
        new() { Label = "PRECIO FINAL", Amount = FinalPrice, IsTotal = true }
    ];

    /// <summary>
    /// Resumen para el cliente. Los conceptos internos —merma, desgaste, gastos y
    /// ganancia— se reparten entre materiales y mano de obra en vez de mostrarse:
    /// enseñarle el margen al cliente sería un problema comercial.
    /// </summary>
    public IReadOnlyList<BudgetBreakdownLine> ClientLines => _clientLines ??=
    [
        new() { Label = "Materiales", Amount = MaterialsCost + Waste + ToolWear },
        new() { Label = "Mano de obra", Amount = Labor + Overhead + Profit },
        new() { Label = "TOTAL", Amount = FinalPrice, IsTotal = true }
    ];
}
