using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Helpers;

namespace MetroCarpinteria.App.Models;

/// <summary>
/// Alícuotas de IVA de uso habitual, en <b>décimas de punto</b>.
/// </summary>
/// <remarks>
/// El 10,5% es una alícuota real y no entra en un entero de porcentajes enteros; guardarlo
/// en décimas evita tener que elegir entre perder el medio punto o usar decimales en un
/// enum. La alícuota igual se guarda como <c>decimal</c> en la base: esto es solo la lista
/// de opciones cómodas del desplegable, y siempre se puede tipear otra.
/// </remarks>
public enum VatRate
{
    None = 0,
    Reduced = 105,
    Standard = 210
}

/// <summary>Lo que el taller pactó con el cliente. Son entradas, no importes.</summary>
public sealed class CommercialTerms
{
    /// <summary>Alícuota en porcentaje. Null o cero es «sin IVA discriminado».</summary>
    public decimal? VatPercent { get; init; }

    public DiscountMode DiscountMode { get; init; } = DiscountMode.None;

    /// <summary>Se lee según <see cref="DiscountMode"/>: porcentaje o importe.</summary>
    public decimal DiscountValue { get; init; }

    /// <summary>Nada pactado: el total es el precio calculado y no hay bloque comercial.</summary>
    public bool IsEmpty =>
        (VatPercent is null or <= 0) && (DiscountMode == DiscountMode.None || DiscountValue <= 0);

    public static CommercialTerms None() => new();

    public static VatRate ToKnownRate(decimal? percent) => percent switch
    {
        null or <= 0 => VatRate.None,
        10.5m => VatRate.Reduced,
        21m => VatRate.Standard,
        _ => VatRate.None
    };

    public static decimal ToPercent(VatRate rate) => (decimal)(int)rate / 10m;
}

/// <summary>Un cobro ya recibido, como lo muestra la pantalla.</summary>
public sealed class ProjectPaymentItem
{
    public int Id { get; init; }
    public PaymentKind Kind { get; init; }
    public decimal Amount { get; init; }
    public PaymentMethod Method { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedAtLocal { get; init; }

    /// <summary>Entró por Caja: no se borra, se compensa.</summary>
    public bool IsLinkedToCash { get; init; }

    public string AmountDisplay => AppCulture.Money(Amount);
    public string KindLabel => PaymentRules.GetKindLabel(Kind);
    public string MethodLabel => PaymentRules.GetMethodLabel(Method);
    public string DateDisplay => AppCulture.ShortDate(CreatedAtLocal);

    public string Summary => $"{KindLabel} · {MethodLabel} · {DateDisplay}";
}

public static class PaymentRules
{
    public static string GetKindLabel(PaymentKind kind) => kind switch
    {
        PaymentKind.Deposit => "Seña",
        PaymentKind.Partial => "Pago a cuenta",
        PaymentKind.Final => "Saldo final",
        _ => kind.ToString()
    };

    public static string GetMethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Efectivo",
        PaymentMethod.Transfer => "Transferencia",
        PaymentMethod.Card => "Tarjeta",
        PaymentMethod.Check => "Cheque",
        _ => "Otro"
    };

    public static IReadOnlyList<PaymentKind> Kinds =>
        [PaymentKind.Deposit, PaymentKind.Partial, PaymentKind.Final];

    public static IReadOnlyList<PaymentMethod> Methods =>
    [
        PaymentMethod.Cash, PaymentMethod.Transfer,
        PaymentMethod.Card, PaymentMethod.Check, PaymentMethod.Other
    ];
}

/// <summary>
/// El tramo comercial del precio: del subtotal calculado al total que paga el cliente.
/// </summary>
public sealed class CommercialBreakdown
{
    private IReadOnlyList<BudgetBreakdownLine>? _lines;

    /// <summary>El precio que salió del cálculo, ya redondeado.</summary>
    public decimal Subtotal { get; init; }

    public decimal Discount { get; init; }

    /// <summary>Sobre esto se aplica el IVA.</summary>
    public decimal TaxableBase { get; init; }

    public decimal Vat { get; init; }
    public decimal Total { get; init; }

    public decimal? VatPercent { get; init; }
    public DiscountMode DiscountMode { get; init; }
    public decimal DiscountValue { get; init; }

    public bool HasDiscount => Discount > 0;
    public bool HasVat => Vat > 0;

    /// <summary>Sin nada pactado, el bloque comercial no se muestra ni se imprime.</summary>
    public bool IsPlain => !HasDiscount && !HasVat;

    public string SubtotalDisplay => AppCulture.Money(Subtotal);
    public string DiscountDisplay => AppCulture.Money(Discount);
    public string TaxableBaseDisplay => AppCulture.Money(TaxableBase);
    public string VatDisplay => AppCulture.Money(Vat);
    public string TotalDisplay => AppCulture.Money(Total);

    /// <summary>Cómo se describe el descuento al lado de su importe.</summary>
    public string DiscountDetail => DiscountMode switch
    {
        DiscountMode.Percentage => $"{AppCulture.Percent(DiscountValue)} sobre el subtotal",
        DiscountMode.Amount => "Importe pactado",
        _ => string.Empty
    };

    public string VatDetail => VatPercent is > 0
        ? $"{AppCulture.Percent(VatPercent.Value)} sobre el neto gravado"
        : string.Empty;

    /// <summary>
    /// El pie que va en los dos documentos. Se omiten las líneas que no aplican: un
    /// presupuesto sin IVA no tiene por qué mostrar «Neto gravado» ni «IVA 0».
    /// </summary>
    public IReadOnlyList<BudgetBreakdownLine> Lines
    {
        get
        {
            if (_lines is not null)
            {
                return _lines;
            }

            var lines = new List<BudgetBreakdownLine>
            {
                new() { Label = "Subtotal", Amount = Subtotal }
            };

            if (HasDiscount)
            {
                // En negativo: el cliente tiene que ver qué se le descontó, no una resta
                // que tiene que hacer él.
                lines.Add(new BudgetBreakdownLine
                {
                    Label = "Descuento",
                    Amount = -Discount,
                    Detail = DiscountDetail
                });
            }

            if (HasVat)
            {
                lines.Add(new BudgetBreakdownLine { Label = "Neto gravado", Amount = TaxableBase });
                lines.Add(new BudgetBreakdownLine { Label = "IVA", Amount = Vat, Detail = VatDetail });
            }

            lines.Add(new BudgetBreakdownLine { Label = "TOTAL", Amount = Total, IsTotal = true });

            return _lines = lines;
        }
    }
}
