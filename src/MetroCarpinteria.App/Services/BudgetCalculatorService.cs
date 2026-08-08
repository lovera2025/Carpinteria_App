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
/// manoDeObra           = valorDia × cantidadDias
/// gastosAdicionales    = manoDeObra × %gastos
/// ganancia             = manoDeObra × %ganancia
/// precioFinal          = suma de los seis
/// </code>
/// Con los valores por defecto equivale a <c>(materiales × 1,25) + (manoDeObra × 1,80)</c>,
/// pero cada concepto se calcula por separado a propósito: la forma simplificada puede
/// diferir en centavos del desglose que ve el usuario.
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
        var labor = Round(input.DailyRate * input.Days);

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
            Rates = rates
        };
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
