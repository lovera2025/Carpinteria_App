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

    /// <summary>Gastos adicionales, como % del jornal del jefe.</summary>
    public decimal OverheadPercent { get; set; } = DefaultOverhead;

    /// <summary>Ganancia, como % del jornal del jefe.</summary>
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
/// En el jefe, <see cref="Loaded"/> incluye gastos y ganancia. En un operario es igual a
/// <see cref="Amount"/>: se suma al costo, sin ese markup. Un ayudante de $ 22.000 por día
/// durante tres días cobra $ 66.000 y le suma exactamente eso al precio.
/// </remarks>
public sealed class LaborShare
{
    public required string Description { get; init; }
    public decimal Days { get; init; }
    public decimal DailyRate { get; init; }

    /// <summary>El jornal pelado: días × valor del día.</summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// Lo que le suma al precio: en el jefe, el jornal más gastos y ganancia; en un
    /// operario, el jornal pelado.
    /// </summary>
    public decimal Loaded { get; init; }

    /// <summary>Es el jefe y no un operario. Solo hay una por cálculo.</summary>
    public bool IsForeman { get; init; }

    public string DaysDisplay => Days == 1m ? "1 día" : $"{AppCulture.Quantity(Days)} días";
    public string RateDisplay => $"{DaysDisplay} × {AppCulture.Money(DailyRate)}";
    public string AmountDisplay => AppCulture.Money(Amount);
    public string LoadedDisplay => AppCulture.Money(Loaded);
}

/// <summary>Cada renglón del desglose, para saber de dónde restar un recorte a mano.</summary>
public enum BudgetLineKind
{
    Materials = 0,
    Waste = 1,
    ToolWear = 2,
    Labor = 3,
    Overhead = 4,
    Profit = 5
}

/// <summary>Claves que se pueden marcar para absorber un recorte, y cómo se guardan.</summary>
public static class BudgetLineKinds
{
    public static IReadOnlyList<BudgetLineKind> Absorbable { get; } =
    [
        BudgetLineKind.Waste,
        BudgetLineKind.ToolWear,
        BudgetLineKind.Overhead,
        BudgetLineKind.Profit
    ];

    public static bool CanAbsorb(BudgetLineKind kind) => kind is
        BudgetLineKind.Waste or BudgetLineKind.ToolWear or BudgetLineKind.Overhead or BudgetLineKind.Profit;

    public static string GetLabel(BudgetLineKind kind) => kind switch
    {
        BudgetLineKind.Materials => "Materiales",
        BudgetLineKind.Waste => "Desperdicio",
        BudgetLineKind.ToolWear => "Desgaste de herramientas",
        BudgetLineKind.Labor => "Mano de obra",
        BudgetLineKind.Overhead => "Gastos adicionales",
        BudgetLineKind.Profit => "Ganancia",
        _ => kind.ToString()
    };

    /// <summary>Null si no hay nada que persistir: el desglose no se tocó.</summary>
    public static string? FormatTargets(IEnumerable<BudgetLineKind> kinds)
    {
        var absorbable = kinds
            .Where(CanAbsorb)
            .Distinct()
            .OrderBy(k => k)
            .ToList();

        return absorbable.Count == 0 ? null : string.Join(",", absorbable);
    }

    public static IReadOnlyList<BudgetLineKind> ParseTargets(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return [];
        }

        var list = new List<BudgetLineKind>();

        foreach (var token in stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse(token, ignoreCase: true, out BudgetLineKind kind)
                && CanAbsorb(kind)
                && !list.Contains(kind))
            {
                list.Add(kind);
            }
        }

        return list;
    }
}

public sealed class BudgetBreakdownLine
{
    public required string Label { get; init; }
    public required decimal Amount { get; init; }
    public string? Detail { get; init; }
    public BudgetLineKind? Kind { get; init; }

    /// <summary>Se puede marcar para absorber un recorte a mano.</summary>
    public bool CanAbsorbAdjustment { get; init; }

    /// <summary>Marca la línea del precio final para que la vista la destaque.</summary>
    public bool IsTotal { get; init; }

    public string AmountDisplay => AppCulture.Money(Amount);
}

/// <summary>Renglón del desglose en la calculadora, con el tilde de recorte.</summary>
public sealed class BreakdownLineItem : ObservableObject
{
    private bool _isSelected;
    private decimal _amount;
    private string? _detail;
    private readonly Action? _onSelectionChanged;

    public BreakdownLineItem(BudgetBreakdownLine line, Action? onSelectionChanged = null)
    {
        Kind = line.Kind;
        Label = line.Label;
        OriginalAmount = line.Amount;
        OriginalDetail = line.Detail;
        _amount = line.Amount;
        _detail = line.Detail;
        CanAbsorbAdjustment = line.CanAbsorbAdjustment;
        IsTotal = line.IsTotal;
        _onSelectionChanged = onSelectionChanged;
    }

    public BudgetLineKind? Kind { get; }
    public string Label { get; }
    public decimal OriginalAmount { get; }
    public string? OriginalDetail { get; }
    public bool CanAbsorbAdjustment { get; }
    public bool IsTotal { get; }

    public decimal Amount
    {
        get => _amount;
        private set
        {
            if (SetProperty(ref _amount, value))
            {
                OnPropertyChanged(nameof(AmountDisplay));
            }
        }
    }

    public string? Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _onSelectionChanged?.Invoke();
            }
        }
    }

    public string AmountDisplay => AppCulture.Money(Amount);

    public void Show(decimal amount, string? detail)
    {
        Amount = amount;
        Detail = detail;
    }

    public void ResetToOriginal() => Show(OriginalAmount, OriginalDetail);
}

/// <summary>Resultado del cálculo, con cada concepto por separado.</summary>
public sealed class BudgetBreakdown
{
    private IReadOnlyList<BudgetBreakdownLine>? _lines;
    private IReadOnlyList<BudgetBreakdownLine>? _compactLines;

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
    /// <b>Solo para la hoja de costos.</b> En el jefe, <see cref="LaborShare.Loaded"/>
    /// incluye gastos y ganancia, así que no puede salir en el papel del cliente.
    /// </remarks>
    public IReadOnlyList<LaborShare> LaborShares { get; init; } = [];

    /// <summary>Líneas que absorbieron un recorte a mano. Vacío si el desglose es el calculado.</summary>
    public IReadOnlyList<BudgetLineKind> AdjustedKinds { get; init; } = [];

    public BudgetRates Rates { get; init; } = new();

    public string FinalPriceDisplay => AppCulture.Money(FinalPrice);

    /// <summary>Hay alguien además del jefe, así que el desglose por persona dice algo.</summary>
    public bool HasWorkers => LaborShares.Count > 1;

    /// <summary>Desglose completo. Uso interno del taller: incluye ganancia y merma.</summary>
    /// <remarks>
    /// Con operarios, la mano de obra se parte persona por persona para que el desglose
    /// de la calculadora no junte a todos en un bloque. La hoja de costos usa
    /// <see cref="CompactLines"/>, que sigue llevando un solo renglón: abajo ya tiene
    /// la tabla por persona y repetirla hacía saltar de A4.
    /// </remarks>
    public IReadOnlyList<BudgetBreakdownLine> Lines => _lines ??= BuildLines(splitLabor: true);

    /// <summary>Igual que <see cref="Lines"/>, pero la mano de obra va en un renglón.</summary>
    public IReadOnlyList<BudgetBreakdownLine> CompactLines => _compactLines ??= BuildLines(splitLabor: false);

    private IReadOnlyList<BudgetBreakdownLine> BuildLines(bool splitLabor)
    {
        var lines = new List<BudgetBreakdownLine>
        {
            Line(BudgetLineKind.Materials, "Materiales", MaterialsCost),
            Line(
                BudgetLineKind.Waste,
                "Desperdicio",
                Waste,
                AppCulture.Percent(Rates.WastePercent) + " de materiales"),
            Line(
                BudgetLineKind.ToolWear,
                "Desgaste de herramientas",
                ToolWear,
                AppCulture.Percent(Rates.ToolWearPercent) + " de materiales")
        };

        if (!HasWorkers || !splitLabor)
        {
            var laborDetail = HasWorkers
                ? $"jefe + {Phrases.Count(LaborShares.Count - 1, "operario", "operarios")} · " +
                  $"{AppCulture.Quantity(LaborShares.Sum(s => s.Days))} jornadas"
                : $"{AppCulture.Quantity(Days)} × {AppCulture.Money(DailyRate)} por día";

            lines.Add(Line(BudgetLineKind.Labor, "Mano de obra", Labor, laborDetail));
        }
        else
        {
            foreach (var share in LaborShares)
            {
                if (share.IsForeman)
                {
                    lines.Add(Line(
                        BudgetLineKind.Labor,
                        share.Description,
                        share.Amount,
                        $"{share.RateDisplay} · gastos {AppCulture.Percent(Rates.OverheadPercent)} · " +
                        $"ganancia {AppCulture.Percent(Rates.ProfitPercent)}"));
                }
                else
                {
                    var name = string.IsNullOrWhiteSpace(share.Description) ? "Operario" : share.Description;
                    lines.Add(Line(BudgetLineKind.Labor, name, share.Amount, share.RateDisplay));
                }
            }
        }

        lines.Add(Line(
            BudgetLineKind.Overhead,
            "Gastos adicionales",
            Overhead,
            AppCulture.Percent(Rates.OverheadPercent) + " del jornal del jefe"));
        lines.Add(Line(
            BudgetLineKind.Profit,
            "Ganancia",
            Profit,
            AppCulture.Percent(Rates.ProfitPercent) + " del jornal del jefe"));
        lines.Add(new() { Label = "PRECIO FINAL", Amount = FinalPrice, IsTotal = true });

        return lines;
    }

    private BudgetBreakdownLine Line(BudgetLineKind kind, string label, decimal amount, string? detail = null) =>
        new()
        {
            Kind = kind,
            Label = label,
            Amount = amount,
            Detail = WithAdjustedNote(detail, kind),
            CanAbsorbAdjustment = BudgetLineKinds.CanAbsorb(kind)
        };

    private string? WithAdjustedNote(string? detail, BudgetLineKind kind)
    {
        if (!AdjustedKinds.Contains(kind))
        {
            return detail;
        }

        return string.IsNullOrWhiteSpace(detail) ? "ajustado" : detail + " · ajustado";
    }
}
