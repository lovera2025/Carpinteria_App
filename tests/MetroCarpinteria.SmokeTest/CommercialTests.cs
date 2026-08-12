using MetroCarpinteria.App.Data.Entities;
using MetroCarpinteria.App.Models;
using MetroCarpinteria.App.Services;

namespace MetroCarpinteria.SmokeTest;

/// <summary>
/// Descuento e IVA sobre el precio calculado. Lo que se protege acá es que agregar el
/// tramo comercial no haya movido un solo peso de lo que la app ya calculaba.
/// </summary>
internal static class CommercialTests
{
    public static void Run(Action<string, Action> run)
    {
        run("Comercial: sin condiciones pactadas, el total es el precio calculado", () =>
        {
            // Es el caso de todo lo que ya está guardado: las tres columnas nuevas quedaron
            // en null, y ningún presupuesto histórico puede cambiar de total.
            var breakdown = Reference();
            var commercial = CommercialTermsService.Apply(breakdown.FinalPrice, null);

            Assert.Equal(commercial.Total, breakdown.FinalPrice, "total sin condiciones");
            Assert.Equal(commercial.Discount, 0m, "descuento");
            Assert.Equal(commercial.Vat, 0m, "IVA");
            Assert.True(commercial.IsPlain, "sin nada pactado no habría que mostrar bloque comercial.");
            Assert.Equal(commercial.Lines.Count, 2, "líneas del bloque comercial");
        });

        run("Comercial: descuento del 15% e IVA del 21% sobre el ejemplo de referencia", () =>
        {
            // $ 287.000 − 15% = $ 243.950; + 21% = $ 295.179,50
            var commercial = CommercialTermsService.Apply(Reference().FinalPrice, new CommercialTerms
            {
                DiscountMode = DiscountMode.Percentage,
                DiscountValue = 15m,
                VatPercent = 21m
            });

            Assert.Equal(commercial.Subtotal, 287000m, "subtotal");
            Assert.Equal(commercial.Discount, 43050m, "descuento");
            Assert.Equal(commercial.TaxableBase, 243950m, "neto gravado");
            Assert.Equal(commercial.Vat, 51229.50m, "IVA");
            Assert.Equal(commercial.Total, 295179.50m, "total");
        });

        run("Comercial: el bloque impreso suma exactamente el total", () =>
        {
            // El mismo invariante que ya tiene el desglose: el total es la suma de los
            // redondeados, no el redondeo de la suma. Con un caso que no cierra redondo.
            var commercial = CommercialTermsService.Apply(166666.64m, new CommercialTerms
            {
                DiscountMode = DiscountMode.Percentage,
                DiscountValue = 13.33m,
                VatPercent = 10.5m
            });

            var shown = commercial.Lines.Where(l => !l.IsTotal).ToList();

            // Subtotal − descuento + IVA. El neto gravado es informativo y no se suma dos
            // veces: por eso se saltea.
            var sum = shown
                .Where(l => l.Label != "Neto gravado")
                .Sum(l => l.Amount);

            Assert.Equal(sum, commercial.Total, "suma de las líneas impresas");
        });

        run("Comercial: un descuento de más deja el total en cero, nunca en negativo", () =>
        {
            // Tipear 150 en vez de 15 no puede terminar con el taller pagándole al cliente.
            var commercial = CommercialTermsService.Apply(287000m, new CommercialTerms
            {
                DiscountMode = DiscountMode.Percentage,
                DiscountValue = 150m
            });

            Assert.Equal(commercial.Discount, 287000m, "descuento acotado al subtotal");
            Assert.Equal(commercial.Total, 0m, "total");

            var byAmount = CommercialTermsService.Apply(1000m, new CommercialTerms
            {
                DiscountMode = DiscountMode.Amount,
                DiscountValue = 5000m
            });

            Assert.Equal(byAmount.Total, 0m, "total con un importe mayor al subtotal");
        });

        run("Comercial: el descuento por importe fijo se aplica tal cual", () =>
        {
            var commercial = CommercialTermsService.Apply(287000m, new CommercialTerms
            {
                DiscountMode = DiscountMode.Amount,
                DiscountValue = 7000m
            });

            Assert.Equal(commercial.Discount, 7000m, "descuento");
            Assert.Equal(commercial.Total, 280000m, "total redondeado para cerrar la venta");
            Assert.False(commercial.HasVat, "no se pactó IVA.");
        });

        run("Comercial: el IVA se calcula sobre el neto, no sobre el subtotal", () =>
        {
            // Es el error clásico: si el IVA saliera del subtotal, el cliente pagaría IVA
            // sobre plata que no se le cobró.
            var commercial = CommercialTermsService.Apply(100000m, new CommercialTerms
            {
                DiscountMode = DiscountMode.Percentage,
                DiscountValue = 10m,
                VatPercent = 21m
            });

            Assert.Equal(commercial.TaxableBase, 90000m, "neto gravado");
            Assert.Equal(commercial.Vat, 18900m, "IVA sobre el neto");
            Assert.True(commercial.Vat != 21000m, "el IVA no puede salir del subtotal.");
        });

        run("Comercial: el margen efectivo muestra lo que queda tras el descuento", () =>
        {
            // Un descuento se come la ganancia antes que ninguna otra cosa, y ése es el
            // número que hay que ver antes de dar la mano.
            var breakdown = Reference();
            var sinDescuento = CommercialTermsService.Apply(breakdown.FinalPrice, null);
            var conDescuento = CommercialTermsService.Apply(breakdown.FinalPrice, new CommercialTerms
            {
                DiscountMode = DiscountMode.Percentage,
                DiscountValue = 15m
            });

            var margenPleno = CommercialTermsService.EffectiveMargin(breakdown, sinDescuento);
            var margenConDescuento = CommercialTermsService.EffectiveMargin(breakdown, conDescuento);

            Assert.NotNull(margenPleno as object, "margen sin descuento");
            Assert.NotNull(margenConDescuento as object, "margen con descuento");

            // Ganancia 27.000 sobre 287.000 ≈ 9,41%.
            Assert.Approximately(margenPleno!.Value, 9.41m, "margen sin descuento", 0.01m);

            // Con 43.050 de descuento la ganancia queda negativa: el trabajo se hace a pérdida.
            Assert.True(
                margenConDescuento!.Value < 0,
                $"un 15% sobre este presupuesto deja el margen en negativo, dio {margenConDescuento}.");
        });

        run("Comercial: las alícuotas conocidas van en décimas de punto", () =>
        {
            // El 10,5% es real y no entra en un porcentaje entero.
            Assert.Equal(CommercialTerms.ToPercent(VatRate.Standard), 21m, "IVA general");
            Assert.Equal(CommercialTerms.ToPercent(VatRate.Reduced), 10.5m, "IVA reducido");
            Assert.Equal(CommercialTerms.ToPercent(VatRate.None), 0m, "sin IVA");

            Assert.Equal(CommercialTerms.ToKnownRate(21m), VatRate.Standard, "reconoce el general");
            Assert.Equal(CommercialTerms.ToKnownRate(10.5m), VatRate.Reduced, "reconoce el reducido");
            Assert.Equal(CommercialTerms.ToKnownRate(null), VatRate.None, "sin alícuota");
        });

        run("Comercial: una alícuota libre también se aplica", () =>
        {
            // Si mañana cambia la alícuota, no hace falta tocar el código.
            var commercial = CommercialTermsService.Apply(100000m, new CommercialTerms { VatPercent = 27m });

            Assert.Equal(commercial.Vat, 27000m, "IVA al 27%");
            Assert.Equal(commercial.Total, 127000m, "total");
        });
    }

    /// <summary>
    /// Las condiciones y los cobros contra la base, con las reglas que tocan Caja.
    /// </summary>
    public static void RunIntegration(
        Action<string, Action> run,
        QuoteService quotes,
        PaymentService payments,
        CashRegisterService cash,
        InventoryService inventory)
    {
        run("Comercial: guardar IVA y descuento actualiza el total del presupuesto", () =>
        {
            var id = NewCalculatedQuote(quotes, inventory, "Mesada con IVA", "Cliente con IVA");

            var before = RequireQuote(quotes, id);
            Assert.True(before.Terms.IsEmpty, "un presupuesto nuevo no tiene condiciones pactadas.");
            Assert.Equal(before.Budget ?? 0m, before.Breakdown!.FinalPrice, "total sin condiciones");

            quotes.SaveCommercialTerms(id, new CommercialTerms
            {
                DiscountMode = DiscountMode.Percentage,
                DiscountValue = 10m,
                VatPercent = 21m
            });

            var after = RequireQuote(quotes, id);

            // El desglose del cálculo no se movió: el tramo comercial va encima.
            Assert.Equal(after.Breakdown!.FinalPrice, before.Breakdown.FinalPrice, "precio calculado");
            Assert.Equal(after.Budget ?? 0m, after.Commercial!.Total, "total guardado");
            Assert.True(after.Commercial.Total > after.Breakdown.FinalPrice, "con 21% el total tendría que subir.");
            Assert.False(after.BudgetAdjustedManually, "aplicar condiciones no es un ajuste a mano.");
        });

        run("Comercial: recalcular después de pactar condiciones las respeta", () =>
        {
            // El orden en que el usuario hace las cosas no puede cambiar el resultado.
            var id = NewCalculatedQuote(quotes, inventory, "Placard recalculado", "Cliente ordenado");

            quotes.SaveCommercialTerms(id, new CommercialTerms { VatPercent = 21m });
            quotes.SaveCalculation(id, 10000m, 4m, 30000m, BudgetRates.Defaults());

            var detail = RequireQuote(quotes, id);

            Assert.Equal(detail.Terms.VatPercent ?? 0m, 21m, "IVA conservado tras recalcular");
            Assert.Equal(detail.Budget ?? 0m, detail.Commercial!.Total, "total con IVA tras recalcular");
        });

        run("Comercial: las condiciones se validan antes de guardarse", () =>
        {
            var id = NewCalculatedQuote(quotes, inventory, "Vitrina validada", "Cliente exigente");

            Assert.Throws(
                () => quotes.SaveCommercialTerms(id, new CommercialTerms { VatPercent = -1m }),
                "IVA");

            Assert.Throws(
                () => quotes.SaveCommercialTerms(id, new CommercialTerms
                {
                    DiscountMode = DiscountMode.Percentage,
                    DiscountValue = 120m
                }),
                "descuento");
        });

        run("Pagos: una seña en efectivo exige caja abierta y queda asentada en el arqueo", () =>
        {
            var id = NewCalculatedQuote(quotes, inventory, "Puerta con seña", "Cliente que adelanta");
            var total = RequireQuote(quotes, id).Budget ?? 0m;

            // Sin caja abierta, el cobro en efectivo rebota con un error propio: la
            // pantalla lo distingue para ofrecer «Abrir caja».
            Assert.Throws<CashRegisterClosedException>(
                () => payments.RegisterPayment(id, PaymentKind.Deposit, 1000m, PaymentMethod.Cash),
                "caja abierta");

            cash.OpenSession(0m, "Apertura para la seña");
            payments.RegisterPayment(id, PaymentKind.Deposit, 1000m, PaymentMethod.Cash, "Adelanto");

            var detail = RequireQuote(quotes, id);
            Assert.Equal(detail.PaidTotal, 1000m, "cobrado");
            Assert.Equal(detail.Balance, total - 1000m, "saldo");
            Assert.True(detail.Payments.Single().IsLinkedToCash, "la seña tendría que estar atada a Caja.");

            // Y el ingreso está en la caja del día.
            var state = cash.GetOpenSessionState()
                ?? throw new InvalidOperationException("No hay caja abierta.");
            Assert.Equal(state.ExpectedBalance, 1000m, "saldo de caja tras la seña");
        });

        run("Pagos: una transferencia no necesita caja abierta", () =>
        {
            // No toda la plata pasa por la caja chica del taller.
            var id = NewCalculatedQuote(quotes, inventory, "Mesa por transferencia", "Cliente bancarizado");

            cash.CloseSession(cash.GetOpenSessionState()!.ExpectedBalance, "Cierre antes de la transferencia");
            Assert.False(cash.HasOpenSession(), "la prueba necesita la caja cerrada.");

            payments.RegisterPayment(id, PaymentKind.Deposit, 500m, PaymentMethod.Transfer);

            var detail = RequireQuote(quotes, id);
            Assert.Equal(detail.PaidTotal, 500m, "cobrado por transferencia");
            Assert.False(detail.Payments.Single().IsLinkedToCash, "una transferencia no toca Caja.");
        });

        run("Pagos: no se puede cobrar más que el saldo", () =>
        {
            // Un saldo negativo después nadie sabe si es una seña doble o un error de tipeo.
            var id = NewCalculatedQuote(quotes, inventory, "Banco sobrecobrado", "Cliente generoso");
            var total = RequireQuote(quotes, id).Budget ?? 0m;

            Assert.Throws(
                () => payments.RegisterPayment(id, PaymentKind.Deposit, total + 1m, PaymentMethod.Transfer),
                "más que el saldo");

            // Justo el saldo sí entra, y deja el trabajo saldado.
            payments.RegisterPayment(id, PaymentKind.Final, total, PaymentMethod.Transfer);

            var detail = RequireQuote(quotes, id);
            Assert.Equal(detail.Balance, 0m, "saldo tras cobrar todo");
            Assert.True(detail.IsFullyPaid, "el trabajo tendría que figurar saldado.");
        });

        run("Pagos: sin precio no se puede cobrar nada", () =>
        {
            var id = quotes.CreateQuote("Trabajo sin cotizar", "Cliente apurado", null).Id;

            Assert.Throws(
                () => payments.RegisterPayment(id, PaymentKind.Deposit, 100m, PaymentMethod.Transfer),
                "precio");
        });

        run("Pagos: anular un cobro de Caja lo compensa, no lo borra del arqueo", () =>
        {
            // Borrar un ingreso de una sesión ya cerrada descuadraría un arqueo que alguien
            // contó y firmó ese día.
            var id = NewCalculatedQuote(quotes, inventory, "Ropero anulado", "Cliente que se arrepintió");

            cash.OpenSession(0m, "Apertura para anular");
            payments.RegisterPayment(id, PaymentKind.Deposit, 2000m, PaymentMethod.Cash);

            var paymentId = RequireQuote(quotes, id).Payments.Single().Id;
            Assert.Equal(cash.GetOpenSessionState()!.IncomeTotal, 2000m, "ingreso asentado");

            payments.CancelPayment(paymentId, "El cliente se arrepintió");

            var detail = RequireQuote(quotes, id);
            Assert.Equal(detail.Payments.Count, 0, "cobros tras anular");

            // El ingreso sigue en el arqueo y aparece una salida que lo compensa.
            var state = cash.GetOpenSessionState()!;
            Assert.Equal(state.IncomeTotal, 2000m, "el ingreso original no se borra");
            Assert.Equal(state.ExpenseTotal, 2000m, "salida compensatoria");
            Assert.Equal(state.ExpectedBalance, 0m, "saldo de caja tras compensar");
        });
    }

    private static int NewCalculatedQuote(
        QuoteService quotes,
        InventoryService inventory,
        string title,
        string client)
    {
        var productId = inventory.CreateProduct($"Material {title}", 100m, 0m, "Metro", 500m).Id;
        var id = quotes.CreateQuote(title, client, null).Id;

        quotes.AddInventoryLine(id, productId, 4m);
        quotes.SaveCalculation(id, 2000m, 2m, 25000m, BudgetRates.Defaults());

        return id;
    }

    private static QuoteDetail RequireQuote(QuoteService quotes, int id) =>
        quotes.GetDetail(id) ?? throw new InvalidOperationException($"No se encontró el presupuesto {id}.");

    /// <summary>El ejemplo de referencia del taller: $ 287.000.</summary>
    private static BudgetBreakdown Reference() => BudgetCalculatorService.Calculate(new BudgetInput
    {
        MaterialsCost = 100000m,
        Days = 3m,
        DailyRate = 30000m,
        Rates = BudgetRates.Defaults()
    });
}
