using MetroCarpinteria.App.Helpers;
using MetroCarpinteria.App.Models;

namespace MetroCarpinteria.App.Services;

/// <summary>
/// Motor de cálculo de presupuestos. No depende de la interfaz ni de la base de datos:
/// para cambiar la fórmula, este es el único archivo que hay que tocar.
/// </summary>
/// <remarks>
/// <code>
/// desperdicio          = materiales × %desperdicio
/// desgasteHerramientas = materiales × %desgaste
/// jornalJefe           = valorDia × cantidadDias
/// jornalOperarios      = Σ(díasOperario × jornalOperario)
/// manoDeObra           = jornalJefe + jornalOperarios
/// gastosAdicionales    = jornalJefe × %gastos
/// ganancia             = jornalJefe × %ganancia
/// precioFinal          = suma de los seis
/// </code>
/// Con los valores por defecto equivale a
/// <c>(materiales × 1,25) + (jornalJefe × 1,80) + jornalOperarios</c>,
/// pero cada concepto se calcula por separado a propósito: la forma simplificada puede
/// diferir en centavos del desglose que ve el usuario.
/// <para>
/// Gastos y ganancia van solo sobre el jornal del jefe. Los operarios se suman al costo,
/// sin ese markup: si no, cargar gente disparaba el presupuesto. Sin operarios la cuenta
/// es la de siempre, que es lo que hace que un presupuesto ya entregado siga dando
/// exactamente el mismo precio.
/// </para>
/// </remarks>
public static class BudgetCalculatorService
{
    public static BudgetBreakdown Calculate(BudgetInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validate(input);

        var rates = input.Rates.Clone();

        var materials = Round(input.MaterialsCost);
        var waste = Round(input.MaterialsCost * AsFraction(rates.WastePercent));
        var toolWear = Round(input.MaterialsCost * AsFraction(rates.ToolWearPercent));

        var foremanLabor = Round(input.DailyRate * input.Days);

        // Cada operario se redondea por su cuenta, igual que cada concepto, para que la
        // tabla por persona sume exactamente la mano de obra del desglose.
        var workerAmounts = input.LaborLines
            .Select(line => Round(line.DailyRate * line.Days))
            .ToList();

        var labor = foremanLabor + workerAmounts.Sum();

        // Gastos y ganancia van sobre el jornal del jefe ya redondeado, para que las
        // líneas del desglose sumen exactamente el precio final. Los operarios no entran
        // en esa base: se cobran al costo.
        var overhead = Round(foremanLabor * AsFraction(rates.OverheadPercent));
        var profit = Round(foremanLabor * AsFraction(rates.ProfitPercent));

        var finalPrice = materials + waste + toolWear + labor + overhead + profit;

        return new BudgetBreakdown
        {
            MaterialsCost = materials,
            Waste = waste,
            ToolWear = toolWear,
            Labor = labor,
            Overhead = overhead,
            Profit = profit,
            FinalPrice = finalPrice,
            Days = input.Days,
            DailyRate = Round(input.DailyRate),
            LaborShares = BuildShares(input, foremanLabor, workerAmounts, overhead, profit),
            Rates = rates
        };
    }

    /// <summary>
    /// Qué pesa cada persona en el precio: el jefe se lleva gastos y ganancia; cada
    /// operario, solo su jornal.
    /// </summary>
    private static List<LaborShare> BuildShares(
        BudgetInput input,
        decimal foremanLabor,
        IReadOnlyList<decimal> workerAmounts,
        decimal overhead,
        decimal profit)
    {
        var shares = new List<LaborShare>(workerAmounts.Count + 1)
        {
            new()
            {
                Description = "Jefe",
                Days = input.Days,
                DailyRate = Round(input.DailyRate),
                Amount = foremanLabor,
                Loaded = foremanLabor + overhead + profit,
                IsForeman = true
            }
        };

        for (var i = 0; i < workerAmounts.Count; i++)
        {
            var line = input.LaborLines[i];
            var amount = workerAmounts[i];

            shares.Add(new LaborShare
            {
                Description = line.Description,
                Days = line.Days,
                DailyRate = Round(line.DailyRate),
                Amount = amount,
                Loaded = amount
            });
        }

        return shares;
    }

    /// <summary>
    /// A qué total de costo hay que llegar para que el recorte en pesos coincida con lo
    /// que se ve en la barra (el total con IVA/descuento).
    /// </summary>
    public static decimal TargetCostTotal(
        decimal calculatedFinalPrice,
        decimal commercialTotal,
        decimal newBudget) =>
        Round(calculatedFinalPrice - (commercialTotal - newBudget));

    /// <summary>
    /// Reparte la diferencia del precio a mano sobre las líneas marcadas. La fórmula
    /// original no se toca: esto es un paso posterior.
    /// </summary>
    public static BudgetBreakdown ApplyPriceAdjustment(
        BudgetBreakdown breakdown,
        IReadOnlyList<BudgetLineKind> targets,
        decimal newFinalPrice)
    {
        ArgumentNullException.ThrowIfNull(breakdown);
        ArgumentNullException.ThrowIfNull(targets);

        if (newFinalPrice <= 0)
        {
            throw new InvalidOperationException("El precio final tiene que ser mayor a cero.");
        }

        var unique = targets
            .Distinct()
            .ToList();

        if (unique.Count == 0)
        {
            return breakdown;
        }

        foreach (var kind in unique)
        {
            if (!BudgetLineKinds.CanAbsorb(kind))
            {
                throw new InvalidOperationException(
                    $"No se puede restar de {BudgetLineKinds.GetLabel(kind)}.");
            }
        }

        var delta = Round(breakdown.FinalPrice - newFinalPrice);
        if (delta == 0)
        {
            return breakdown;
        }

        var amounts = new Dictionary<BudgetLineKind, decimal>
        {
            [BudgetLineKind.Waste] = breakdown.Waste,
            [BudgetLineKind.ToolWear] = breakdown.ToolWear,
            [BudgetLineKind.Overhead] = breakdown.Overhead,
            [BudgetLineKind.Profit] = breakdown.Profit
        };

        var adjusted = Distribute(unique, amounts, delta);
        var waste = adjusted[BudgetLineKind.Waste];
        var toolWear = adjusted[BudgetLineKind.ToolWear];
        var overhead = adjusted[BudgetLineKind.Overhead];
        var profit = adjusted[BudgetLineKind.Profit];
        var finalPrice = breakdown.MaterialsCost + waste + toolWear + breakdown.Labor + overhead + profit;

        return new BudgetBreakdown
        {
            MaterialsCost = breakdown.MaterialsCost,
            Waste = waste,
            ToolWear = toolWear,
            Labor = breakdown.Labor,
            Overhead = overhead,
            Profit = profit,
            FinalPrice = finalPrice,
            Days = breakdown.Days,
            DailyRate = breakdown.DailyRate,
            LaborShares = ReloadedShares(breakdown.LaborShares, overhead, profit),
            AdjustedKinds = unique,
            Rates = breakdown.Rates
        };
    }

    /// <param name="delta">Positivo baja las líneas; negativo las sube.</param>
    private static Dictionary<BudgetLineKind, decimal> Distribute(
        IReadOnlyList<BudgetLineKind> targets,
        IReadOnlyDictionary<BudgetLineKind, decimal> amounts,
        decimal delta)
    {
        var result = amounts.ToDictionary(pair => pair.Key, pair => pair.Value);

        if (delta > 0)
        {
            var capacity = targets.Sum(kind => amounts[kind]);
            if (capacity < delta)
            {
                var names = string.Join(" + ", targets.Select(BudgetLineKinds.GetLabel));
                throw new InvalidOperationException(
                    $"{names} cubren {AppCulture.Money(capacity)}; faltan {AppCulture.Money(delta - capacity)}.");
            }

            var remaining = delta;
            for (var i = 0; i < targets.Count; i++)
            {
                var kind = targets[i];
                var share = i == targets.Count - 1
                    ? remaining
                    : Round(delta * amounts[kind] / capacity);

                result[kind] = amounts[kind] - share;
                remaining -= share;
            }
        }
        else
        {
            var add = -delta;
            var baseSum = targets.Sum(kind => amounts[kind]);
            var remaining = add;

            for (var i = 0; i < targets.Count; i++)
            {
                var kind = targets[i];
                decimal share;
                if (i == targets.Count - 1)
                {
                    share = remaining;
                }
                else if (baseSum == 0)
                {
                    share = Round(add / targets.Count);
                }
                else
                {
                    share = Round(add * amounts[kind] / baseSum);
                }

                result[kind] = amounts[kind] + share;
                remaining -= share;
            }
        }

        return result;
    }

    private static IReadOnlyList<LaborShare> ReloadedShares(
        IReadOnlyList<LaborShare> shares,
        decimal overhead,
        decimal profit)
    {
        return shares
            .Select(share => share.IsForeman
                ? new LaborShare
                {
                    Description = share.Description,
                    Days = share.Days,
                    DailyRate = share.DailyRate,
                    Amount = share.Amount,
                    Loaded = share.Amount + overhead + profit,
                    IsForeman = true
                }
                : share)
            .ToList();
    }

    private static void Validate(BudgetInput input)
    {
        if (input.MaterialsCost < 0)
        {
            throw new InvalidOperationException("El costo de materiales no puede ser negativo.");
        }

        if (input.Days < 0)
        {
            throw new InvalidOperationException("Los días de trabajo no pueden ser negativos.");
        }

        if (input.DailyRate < 0)
        {
            throw new InvalidOperationException("El valor del jornal no puede ser negativo.");
        }

        foreach (var line in input.LaborLines)
        {
            // El nombre se usa en el mensaje: con tres operarios cargados, «los días no
            // pueden ser negativos» no dice cuál hay que corregir.
            var who = string.IsNullOrWhiteSpace(line.Description) ? "un operario" : line.Description;

            if (line.Days < 0)
            {
                throw new InvalidOperationException($"Los días de {who} no pueden ser negativos.");
            }

            if (line.DailyRate < 0)
            {
                throw new InvalidOperationException($"El jornal de {who} no puede ser negativo.");
            }
        }

        ValidateRate(input.Rates.WastePercent, "desperdicio");
        ValidateRate(input.Rates.ToolWearPercent, "desgaste de herramientas");
        ValidateRate(input.Rates.OverheadPercent, "gastos adicionales");
        ValidateRate(input.Rates.ProfitPercent, "ganancia");
    }

    private static void ValidateRate(decimal percent, string name)
    {
        if (percent < 0)
        {
            throw new InvalidOperationException($"El porcentaje de {name} no puede ser negativo.");
        }

        if (percent > 1000)
        {
            throw new InvalidOperationException($"El porcentaje de {name} es demasiado alto.");
        }
    }

    private static decimal AsFraction(decimal percent) => percent / 100m;

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
