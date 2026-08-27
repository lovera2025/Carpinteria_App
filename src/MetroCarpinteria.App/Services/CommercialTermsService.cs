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
    /// El mismo bloque, pero armado al revés: desde el total que paga el cliente.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hace falta porque el precio se puede fijar a mano —el taller redondea para cerrar la
    /// venta— y ese número es el que va impreso. Derivando el bloque del cálculo, el papel
    /// mostraba una columna que cerraba en un número y abajo un TOTAL distinto: con IVA del
    /// 21% sobre un cálculo de $ 100.000, fijar $ 110.000 dejaba el bloque sumando
    /// $ 121.000 contra un TOTAL de $ 110.000.
    /// </para>
    /// <para>
    /// El neto gravado se despeja del total y el IVA es <b>lo que sobra</b>, no un
    /// <c>Round(base × alícuota)</c> aparte. Así la columna cierra exacto por construcción.
    /// El IVA mostrado puede quedar a un centavo del que daría la multiplicación; entre eso
    /// y que el papel no sume, no hay discusión.
    /// </para>
    /// </remarks>
    public static CommercialBreakdown ForTotal(decimal total, CommercialTerms? terms)
    {
        if (total < 0)
        {
            throw new InvalidOperationException("El total no puede ser negativo.");
        }

        terms ??= CommercialTerms.None();

        // Sin nada pactado el bloque es el total pelado, exactamente igual que Apply.
        if (terms.IsEmpty)
        {
            return Apply(total, terms);
        }

        var vatPercent = terms.VatPercent is > 0 ? terms.VatPercent.Value : (decimal?)null;

        var taxableBase = vatPercent is null
            ? total
            : Round(total / (1m + vatPercent.Value / 100m));

        var vat = total - taxableBase;
        var subtotal = SubtotalForTaxableBase(taxableBase, terms);
        var discount = subtotal - taxableBase;

        return new CommercialBreakdown
        {
            Subtotal = subtotal,
            Discount = discount,
            TaxableBase = taxableBase,
            Vat = vat,
            Total = total,
            VatPercent = vatPercent,
            DiscountMode = discount > 0 ? terms.DiscountMode : DiscountMode.None,
            DiscountValue = terms.DiscountValue
        };
    }

    /// <summary>
    /// Qué subtotal hay que tener para que, restado el descuento, quede exactamente
    /// <paramref name="taxableBase"/>.
    /// </summary>
    /// <remarks>
    /// Con descuento en porcentaje la cuenta salta de a centavos por el redondeo, así que
    /// la división da un estimado y se busca el valor exacto a su alrededor. Si ninguno
    /// cierra —pasa cuando el salto se lleva por delante el objetivo— queda el estimado,
    /// que es el más cercano posible.
    /// </remarks>
    private static decimal SubtotalForTaxableBase(decimal taxableBase, CommercialTerms terms)
    {
        if (terms.DiscountValue <= 0)
        {
            return taxableBase;
        }

        // Con el neto en cero el descuento se comió el trabajo entero, y esa cuenta no se
        // puede deshacer: no hay forma de saber de cuánto era. Se devuelve cero antes que
        // inventar un subtotal que nadie cotizó. Es el único caso en el que el ida y vuelta
        // contra Apply no cierra, y no llega acá desde la app: GetDetail solo arma el bloque
        // al revés cuando hay un precio mayor a cero.
        if (taxableBase <= 0m)
        {
            return 0m;
        }

        if (terms.DiscountMode == DiscountMode.Amount)
        {
            return taxableBase + Round(terms.DiscountValue);
        }

        if (terms.DiscountMode != DiscountMode.Percentage)
        {
            return taxableBase;
        }

        var rest = 1m - (terms.DiscountValue / 100m);

        // Un descuento del 100% deja el neto en cero y cualquier subtotal lo cumple. Se
        // devuelve el que no inventa un descuento mayor que el trabajo.
        if (rest <= 0m)
        {
            return taxableBase;
        }

        var estimate = Round(taxableBase / rest);

        for (var cents = 0; cents <= 5; cents++)
        {
            var step = cents / 100m;

            foreach (var candidate in new[] { estimate + step, estimate - step })
            {
                if (candidate >= 0m
                    && candidate - Round(candidate * terms.DiscountValue / 100m) == taxableBase)
                {
                    return candidate;
                }
            }
        }

        return estimate;
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
