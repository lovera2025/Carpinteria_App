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
/// manoDeObra           = (valorDia × cantidadDias) + Σ(díasOperario × jornalOperario)
/// gastosAdicionales    = manoDeObra × %gastos
/// ganancia             = manoDeObra × %ganancia
/// precioFinal          = suma de los seis
/// </code>
/// Con los valores por defecto equivale a <c>(materiales × 1,25) + (manoDeObra × 1,80)</c>,
/// pero cada concepto se calcula por separado a propósito: la forma simplificada puede
/// diferir en centavos del desglose que ve el usuario.
/// <para>
/// La mano de obra es el jefe —<c>valorDia × cantidadDias</c>— más un renglón por operario.
/// Sin operarios la cuenta es la de siempre, que es lo que hace que un presupuesto ya
/// entregado siga dando exactamente el mismo precio.
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

        // Los porcentajes de mano de obra se aplican sobre el jornal ya redondeado, para
        // que las líneas del desglose sumen exactamente el precio final y no quede un
        // total que no cierra con lo que se muestra.
        var overhead = Round(labor * AsFraction(rates.OverheadPercent));
        var profit = Round(labor * AsFraction(rates.ProfitPercent));

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
            LaborShares = BuildShares(input, foremanLabor, workerAmounts, labor, overhead + profit),
            Rates = rates
        };
    }

    /// <summary>
    /// Reparte gastos y ganancia entre las personas, en proporción a lo que cobra cada una.
    /// </summary>
    /// <remarks>
    /// El reparto sale de los totales ya calculados y no de volver a aplicar los porcentajes:
    /// así la columna cierra clavada con el desglose en vez de diferir en centavos. Los que
    /// sobran al redondear van al jefe, que se calcula por diferencia — siempre está, es
    /// normalmente la línea más grande, y es donde menos se nota.
    /// </remarks>
    private static List<LaborShare> BuildShares(
        BudgetInput input,
        decimal foremanLabor,
        IReadOnlyList<decimal> workerAmounts,
        decimal labor,
        decimal markup)
    {
        var loadedTotal = labor + markup;
        var shares = new List<LaborShare>(workerAmounts.Count + 1);
        var assigned = 0m;

        for (var i = 0; i < workerAmounts.Count; i++)
        {
            var line = input.LaborLines[i];
            var amount = workerAmounts[i];

            // labor > 0 salvo que nadie trabaje, y ahí todos los importes son cero igual.
            var loaded = labor <= 0 ? 0m : Round(amount / labor * loadedTotal);
            assigned += loaded;

            shares.Add(new LaborShare
            {
                Description = line.Description,
                Days = line.Days,
                DailyRate = Round(line.DailyRate),
                Amount = amount,
                Loaded = loaded
            });
        }

        shares.Insert(0, new LaborShare
        {
            Description = "Jefe",
            Days = input.Days,
            DailyRate = Round(input.DailyRate),
            Amount = foremanLabor,
            Loaded = loadedTotal - assigned,
            IsForeman = true
        });

        return shares;
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
