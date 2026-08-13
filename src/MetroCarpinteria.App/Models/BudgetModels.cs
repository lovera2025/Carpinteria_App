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

/// <summary>Un operario cotizado: sus días y su jornal.</summary>
public sealed class LaborLineInput
{
    public required string Description { get; init; }
    public decimal Days { get; init; }
    public decimal DailyRate { get; init; }
}

public sealed class BudgetInput
{
    public decimal MaterialsCost { get; init; }

    /// <summary>Días del jefe.</summary>
    public decimal Days { get; init; }

    /// <summary>Jornal del jefe.</summary>
    public decimal DailyRate { get; init; }

    /// <summary>
    /// Los operarios, si los hay. Vacío es «lo hace el jefe solo», que es como se leen
    /// todos los presupuestos anteriores a que esto existiera.
    /// </summary>
    public IReadOnlyList<LaborLineInput> LaborLines { get; init; } = [];

    public BudgetRates Rates { get; init; } = new();
}

/// <summary>
/// Lo que aporta una persona a la mano de obra, y lo que termina pesando en el precio.
/// </summary>
/// <remarks>
/// La diferencia entre <see cref="Amount"/> y <see cref="Loaded"/> es el dato que sirve para
/// decidir a quién poner en un trabajo: un ayudante de $ 22.000 por día durante tres días
/// cobra $ 66.000, pero arrastra su parte de gastos y ganancia y le suma $ 118.800 al precio.
/// </remarks>
public sealed class LaborShare
{
    public required string Description { get; init; }
    public decimal Days { get; init; }
    public decimal DailyRate { get; init; }

    /// <summary>El jornal pelado: días × valor del día.</summary>
    public decimal Amount { get; init; }

    /// <summary>El jornal más la parte proporcional de gastos adicionales y ganancia.</summary>
    public decimal Loaded { get; init; }

    /// <summary>Es el jefe y no un operario. Solo hay una por cálculo.</summary>
    public bool IsForeman { get; init; }

    public string DaysDisplay => Days == 1m ? "1 día" : $"{AppCulture.Quantity(Days)} días";
    public string RateDisplay => $"{DaysDisplay} × {AppCulture.Money(DailyRate)}";
    public string AmountDisplay => AppCulture.Money(Amount);
    public string LoadedDisplay => AppCulture.Money(Loaded);
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

    public decimal MaterialsCost { get; init; }
    public decimal Waste { get; init; }
    public decimal ToolWear { get; init; }
    public decimal Labor { get; init; }
    public decimal Overhead { get; init; }
    public decimal Profit { get; init; }
    public decimal FinalPrice { get; init; }

    /// <summary>Días del jefe.</summary>
    public decimal Days { get; init; }

    /// <summary>Jornal del jefe.</summary>
    public decimal DailyRate { get; init; }

    /// <summary>
    /// La mano de obra persona por persona: el jefe primero y después cada operario, con lo
    /// que cada uno pesa en el precio final. Siempre trae al menos la línea del jefe.
    /// </summary>
    /// <remarks>
    /// <b>Solo para la hoja de costos.</b> <see cref="LaborShare.Loaded"/> incluye la parte
    /// proporcional de la ganancia, así que no puede salir en el papel del cliente.
    /// </remarks>
    public IReadOnlyList<LaborShare> LaborShares { get; init; } = [];

    public BudgetRates Rates { get; init; } = new();

    public string FinalPriceDisplay => AppCulture.Money(FinalPrice);

    /// <summary>Hay alguien además del jefe, así que el desglose por persona dice algo.</summary>
    public bool HasWorkers => LaborShares.Count > 1;

    /// <summary>
    /// La aclaración del renglón «Mano de obra» del desglose.
    /// </summary>
    /// <remarks>
    /// Con operarios no puede seguir diciendo «5 × $ 40.000 por día», que describiría a una
    /// sola persona. Resume quiénes y cuántas jornadas; el detalle uno por uno va en su
    /// propia tabla. Sin operarios queda la frase de siempre, así que un presupuesto viejo
    /// se imprime idéntico.
    /// </remarks>
    private string LaborDetail
    {
        get
        {
            if (!HasWorkers)
            {
                return $"{AppCulture.Quantity(Days)} × {AppCulture.Money(DailyRate)} por día";
            }

            var workdays = LaborShares.Sum(s => s.Days);

            return $"jefe + {Phrases.Count(LaborShares.Count - 1, "operario", "operarios")} · " +
                   $"{AppCulture.Quantity(workdays)} jornadas";
        }
    }

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
            Detail = LaborDetail
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
}
