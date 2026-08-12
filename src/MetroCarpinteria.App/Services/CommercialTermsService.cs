using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Models;

namespace MetroCarpinteria.App.Services;

/// <summary>
/// Aplica descuento e IVA sobre un precio ya calculado.
/// </summary>
/// <remarks>
/// <para>
/// Es una <b>segunda etapa</b>, no un cambio en la fórmula:
/// <see cref="BudgetCalculatorService"/> queda exactamente como estaba y sigue siendo el
/// único lugar donde vive el cálculo del precio. Meter el IVA adentro habría obligado a
/// rehacer el invariante de redondeo que hace que el desglose sume exacto, y ese
/// invariante es lo que sostiene que el papel del cliente cierre.
/// </para>
/// <para>
/// Mismo criterio de redondeo que el desglose: cada término se redondea por separado y el
/// total es la suma de los redondeados, nunca el redondeo de la suma. Por eso la columna
/// impresa cierra al centavo.
/// </para>
/// </remarks>
public static class CommercialTermsService
{
    public static CommercialBreakdown Apply(decimal subtotal, CommercialTerms? terms)
    {
        if (subtotal < 0)
        {
            throw new InvalidOperationException("El subtotal no puede ser negativo.");
        }

        terms ??= CommercialTerms.None();

        var discount = CalculateDiscount(subtotal, terms);
        var taxableBase = subtotal - discount;
        var vatPercent = terms.VatPercent is > 0 ? terms.VatPercent.Value : (decimal?)null;
        var vat = vatPercent is null ? 0m : Round(taxableBase * vatPercent.Value / 100m);

        return new CommercialBreakdown
        {
            Subtotal = subtotal,
            Discount = discount,
            TaxableBase = taxableBase,
            Vat = vat,
            Total = taxableBase + vat,
            VatPercent = vatPercent,
            DiscountMode = discount > 0 ? terms.DiscountMode : DiscountMode.None,
            DiscountValue = terms.DiscountValue
        };
    }

    /// <summary>
    /// El descuento nunca puede superar el subtotal ni ser negativo.
    /// </summary>
    /// <remarks>
    /// Acotarlo acá y no validarlo antes es deliberado: un descuento tipeado de más
    /// —un 150% en vez de un 15%— tiene que dar un total de cero, no uno negativo que el
    /// taller terminaría pagándole al cliente. El formulario avisa aparte.
    /// </remarks>
    private static decimal CalculateDiscount(decimal subtotal, CommercialTerms terms)
    {
        if (terms.DiscountValue <= 0)
        {
            return 0m;
        }

        var raw = terms.DiscountMode switch
        {
            DiscountMode.Percentage => Round(subtotal * terms.DiscountValue / 100m),
            DiscountMode.Amount => Round(terms.DiscountValue),
            _ => 0m
        };

        return Math.Clamp(raw, 0m, subtotal);
    }

    /// <summary>
    /// Lo que queda de ganancia después de resignar el descuento, sobre el neto gravado.
    /// </summary>
    /// <remarks>
    /// Es el número que hace útil la hoja de costos: un descuento del 15% sobre un margen
    /// del 30% deja 12%, y eso hay que verlo <b>antes</b> de dar la mano, no al cerrar el
    /// mes. Null cuando no hay base sobre la cual medirlo.
    /// </remarks>
    public static decimal? EffectiveMargin(BudgetBreakdown breakdown, CommercialBreakdown commercial)
    {
        if (commercial.TaxableBase <= 0)
        {
            return null;
        }

        return Round((breakdown.Profit - commercial.Discount) / commercial.TaxableBase * 100m);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
