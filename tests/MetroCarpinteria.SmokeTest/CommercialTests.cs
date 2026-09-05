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

        run("Comercial: armar el bloque al revés devuelve el mismo bloque", () =>
        {
            // Es la regresión que protege a todo lo ya entregado: en el camino normal el
            // precio guardado sale de Apply, así que ForTotal sobre ese precio tiene que
            // reconstruir exactamente lo mismo. Si esto se mueve, cambian de número
            // presupuestos que el cliente ya tiene en la mano.
            CommercialTerms[] casos =
            [
                CommercialTerms.None(),
                new() { VatPercent = 21m },
                new() { VatPercent = 10.5m },
                new() { DiscountMode = DiscountMode.Percentage, DiscountValue = 15m },
                new() { DiscountMode = DiscountMode.Amount, DiscountValue = 12345.67m },
                new() { DiscountMode = DiscountMode.Percentage, DiscountValue = 15m, VatPercent = 21m },
                new() { DiscountMode = DiscountMode.Amount, DiscountValue = 5000m, VatPercent = 10.5m }
            ];

            decimal[] precios = [287000m, 92500m, 166666.64m, 1m, 999999.99m];

            foreach (var terms in casos)
            {
                foreach (var precio in precios)
                {
                    var ida = CommercialTermsService.Apply(precio, terms);
                    var vuelta = CommercialTermsService.ForTotal(ida.Total, terms);

                    var que = $"{precio} con {Describe(terms)}";

                    if (ida.Total == 0m)
                    {
                        // El descuento se comió el trabajo entero: desde un total de cero no
                        // hay forma de saber de cuánto era, y no llega a pasar en la app
                        // porque el bloque se arma al revés solo con un precio mayor a cero.
                        // Lo que sí se exige es que no invente un subtotal.
                        Assert.Equal(vuelta.Subtotal, 0m, $"no puede inventar un subtotal — {que}");
                        continue;
                    }

                    Assert.Equal(vuelta.Total, ida.Total, $"total ida y vuelta — {que}");
                    Assert.Equal(vuelta.Subtotal, ida.Subtotal, $"subtotal ida y vuelta — {que}");
                    Assert.Equal(vuelta.Discount, ida.Discount, $"descuento ida y vuelta — {que}");
                    Assert.Equal(vuelta.TaxableBase, ida.TaxableBase, $"neto ida y vuelta — {que}");
                    Assert.Equal(vuelta.Vat, ida.Vat, $"IVA ida y vuelta — {que}");
                }
            }
        });

        run("Comercial: el bloque armado al revés cierra exacto en el total pedido", () =>
        {
            // Acá el total NO sale de un cálculo: es un precio redondeado a mano, que es
            // justo el caso donde el papel no cerraba.
            CommercialTerms[] casos =
            [
                new() { VatPercent = 21m },
                new() { VatPercent = 10.5m },
                new() { DiscountMode = DiscountMode.Percentage, DiscountValue = 15m, VatPercent = 21m },
                new() { DiscountMode = DiscountMode.Amount, DiscountValue = 5000m, VatPercent = 21m }
            ];

            decimal[] totales = [110000m, 100000m, 87654.321m, 250000m, 33333.33m];

            foreach (var terms in casos)
            {
                foreach (var total in totales)
                {
                    var bloque = CommercialTermsService.ForTotal(total, terms);
                    var que = $"{total} con {Describe(terms)}";

                    Assert.Equal(bloque.Total, total, $"el bloque tiene que cerrar en el total pedido — {que}");

                    // Y la columna impresa suma ese mismo número: subtotal − descuento + IVA.
                    // El neto gravado es informativo y no se suma dos veces.
                    var impreso = bloque.Subtotal - bloque.Discount + bloque.Vat;
                    Assert.Equal(impreso, total, $"la columna impresa tiene que sumar el total — {que}");
                }
            }
        });

        run("Comercial: con IVA, fijar un precio a mano deja el bloque cerrando en ese precio", () =>
        {
            // El caso concreto que estaba mal: cálculo $ 100.000 + IVA 21% da $ 121.000, y
            // fijar $ 110.000 dejaba el bloque sumando 121.000 contra un TOTAL de 110.000.
            var terms = new CommercialTerms { VatPercent = 21m };
            var bloque = CommercialTermsService.ForTotal(110000m, terms);

            Assert.Equal(bloque.TaxableBase, 90909.09m, "neto gravado");
            Assert.Equal(bloque.Vat, 19090.91m, "IVA");
            Assert.Equal(bloque.Total, 110000m, "total");
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
        InventoryService inventory,
        ProjectService projects)
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

        run("Pagos: una seña en efectivo entra a la caja sin abrir nada", () =>
        {
            var id = NewCalculatedQuote(quotes, inventory, "Puerta con seña", "Cliente que adelanta");
            var total = RequireQuote(quotes, id).Budget ?? 0m;
            var before = cash.GetBalance();

            payments.RegisterPayment(id, PaymentKind.Deposit, 1000m, PaymentMethod.Cash, "Adelanto");

            var detail = RequireQuote(quotes, id);
            Assert.Equal(detail.PaidTotal, 1000m, "cobrado");
            Assert.Equal(detail.Balance, total - 1000m, "saldo");
            Assert.True(detail.Payments.Single().IsLinkedToCash, "la seña tendría que estar asentada en Caja.");

            var after = cash.GetBalance();
            Assert.Equal(after.Balance, before.Balance + 1000m, "saldo de la caja tras la seña");
            Assert.Equal(after.CashOnHand, before.CashOnHand + 1000m, "efectivo tras la seña");
        });

        run("Pagos: una transferencia también entra a la caja, y no la cuenta como efectivo", () =>
        {
            // Este es el problema que reportó el taller: solo asentaba el efectivo, así que
            // una seña por transferencia bajaba el saldo del cliente y no figuraba en
            // ningún lado. Ahora entra, con su medio, sin sumarse a los billetes del cajón.
            var id = NewCalculatedQuote(quotes, inventory, "Mesa por transferencia", "Cliente bancarizado");
            var before = cash.GetBalance();

            payments.RegisterPayment(id, PaymentKind.Deposit, 500m, PaymentMethod.Transfer);

            var detail = RequireQuote(quotes, id);
            Assert.Equal(detail.PaidTotal, 500m, "cobrado por transferencia");
            Assert.True(detail.Payments.Single().IsLinkedToCash, "una transferencia también deja asiento.");

            var after = cash.GetBalance();
            Assert.Equal(after.Balance, before.Balance + 500m, "saldo de la caja tras la transferencia");
            Assert.Equal(after.CashOnHand, before.CashOnHand, "el efectivo del cajón no se tocó");
        });

        run("Pagos: el movimiento dice de quién es la plata", () =>
        {
            // «Seña: Mostrador» sin nombre no le dice al taller de quién es el dinero. El
            // proyecto queda vinculado además del texto, para poder cruzarlo.
            var id = NewCalculatedQuote(quotes, inventory, "Bajomesada rastreable", "María González");

            payments.RegisterPayment(id, PaymentKind.Deposit, 700m, PaymentMethod.Transfer);

            var movement = cash.GetMovements().First(m => m.ProjectId == id);
            Assert.True(movement.Reason.Contains("María González", StringComparison.Ordinal),
                "el motivo tendría que nombrar al cliente.");
            Assert.Equal(movement.OriginDisplay, "Bajomesada rastreable · María González", "origen del movimiento");
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

        run("Pagos: anular un cobro no borra nada, ni en Caja ni en el trabajo", () =>
        {
            var id = NewCalculatedQuote(quotes, inventory, "Ropero anulado", "Cliente que se arrepintió");
            var before = cash.GetBalance();

            payments.RegisterPayment(id, PaymentKind.Deposit, 2000m, PaymentMethod.Cash);

            var paymentId = RequireQuote(quotes, id).Payments.Single().Id;
            Assert.Equal(cash.GetBalance().Income, before.Income + 2000m, "ingreso asentado");

            payments.CancelPayment(paymentId, "El cliente se arrepintió");

            var detail = RequireQuote(quotes, id);

            // El cobro sigue en la ficha del trabajo, marcado. Antes se borraba la fila y
            // del proyecto no quedaba rastro de que ese cobro hubiera existido.
            Assert.Equal(detail.Payments.Count, 1, "el cobro anulado sigue en la ficha");

            var cancelled = detail.Payments.Single();
            Assert.True(cancelled.IsCancelled, "tendría que figurar como anulado.");
            Assert.Equal(detail.PaidTotal, 0m, "un cobro anulado no cuenta para el saldo");
            Assert.False(detail.HasPayments, "sin cobros vigentes");

            // Lo que se lee en pantalla: si un cobro anulado se ve igual que uno vigente,
            // el renglón está mintiendo aunque el saldo esté bien.
            Assert.True(
                cancelled.Summary.Contains("ANULADO", StringComparison.Ordinal),
                "el renglón tiene que decir que está anulado.");
            Assert.True(
                cancelled.CancelNote.Contains("se arrepintió", StringComparison.Ordinal),
                "el motivo de la anulación tiene que quedar a la vista.");

            // Y en Caja el ingreso original queda, con una salida que lo compensa.
            var after = cash.GetBalance();
            Assert.Equal(after.Income, before.Income + 2000m, "el ingreso original no se borra");
            Assert.Equal(after.Expense, before.Expense + 2000m, "salida compensatoria");
            Assert.Equal(after.Balance, before.Balance, "el saldo vuelve a donde estaba");

            // Anular dos veces no duplica la compensación.
            Assert.Throws(() => payments.CancelPayment(paymentId, "de nuevo"), "ya estaba anulado");
        });

        run("Comercial: con IVA, un precio fijado a mano deja el papel cerrando", () =>
        {
            // El bloque comercial salía del cálculo y el TOTAL del precio guardado: con IVA
            // pactado, el papel del cliente mostraba una columna que sumaba $ 111.925 y
            // abajo un TOTAL de $ 110.000. Las dos cifras en la misma hoja.
            var id = NewCalculatedQuote(quotes, inventory, "Bajomesada con IVA", "Cliente que factura");

            quotes.SaveCommercialTerms(id, new CommercialTerms { VatPercent = 21m });
            Assert.Equal(RequireQuote(quotes, id).Budget ?? 0m, 111925m, "total con IVA antes de redondear");

            quotes.SetFinalPrice(id, 110000m);

            var detail = RequireQuote(quotes, id);
            var commercial = detail.Commercial
                ?? throw new InvalidOperationException("Con IVA pactado tendría que haber bloque comercial.");

            Assert.Equal(commercial.Total, detail.Budget ?? 0m, "el bloque tiene que cerrar en el TOTAL impreso");
            Assert.Equal(detail.PrintedTotal, 110000m, "el número grande del papel");
            Assert.Equal(commercial.TaxableBase, 90909.09m, "neto gravado");
            Assert.Equal(commercial.Vat, 19090.91m, "IVA");
        });

        run("Comercial: con IVA y recorte, el desglose recortado suma el subtotal del bloque", () =>
        {
            // La otra mitad: el recorte se aplicaba restando del costo neto una diferencia
            // medida sobre el total con IVA, así que caía $ 1.909 más abajo de lo que
            // correspondía y el desglose no coincidía con el bloque.
            var id = NewCalculatedQuote(quotes, inventory, "Vitrina recortada", "Cliente que redondea");

            quotes.SaveCommercialTerms(id, new CommercialTerms { VatPercent = 21m });
            var sinRecortar = RequireQuote(quotes, id).Breakdown!.Profit;

            quotes.SetFinalPrice(id, 110000m, [BudgetLineKind.Profit]);

            var detail = RequireQuote(quotes, id);
            var commercial = detail.Commercial!;

            Assert.Equal(commercial.Total, detail.Budget ?? 0m, "el bloque cierra en el TOTAL impreso");
            Assert.Equal(
                detail.Breakdown!.FinalPrice,
                commercial.Subtotal,
                "el desglose recortado tiene que sumar el subtotal del bloque");

            // Y el recorte salió de ganancia, que es lo que se marcó.
            Assert.Equal(detail.Breakdown.Profit, sinRecortar - 1590.91m, "ganancia recortada");
            Assert.Equal(detail.UnadjustedBreakdown!.Profit, sinRecortar, "el cálculo original sigue ahí");
        });

        run("Pagos: no se puede fijar un precio por debajo de lo ya cobrado", () =>
        {
            // La otra mitad de «no se puede cobrar más que el saldo». Sin esto se tomaba la
            // seña, se cerraba el trabajo más barato, y el panel decía «Cobrado por completo»
            // con saldo cero: la plata a devolver no figuraba en ningún lado.
            var id = NewCalculatedQuote(quotes, inventory, "Alacena renegociada", "Cliente que negocia");
            Assert.Equal(RequireQuote(quotes, id).Budget ?? 0m, 92500m, "precio de partida");

            payments.RegisterPayment(id, PaymentKind.Deposit, 40000m, PaymentMethod.Transfer);

            Assert.Throws(() => quotes.SetFinalPrice(id, 30000m), "Ya se cobraron");
            Assert.Equal(RequireQuote(quotes, id).Budget ?? 0m, 92500m, "el precio no tendría que moverse");

            // Justo lo cobrado sí entra: deja el trabajo saldado y sin plata a favor.
            quotes.SetFinalPrice(id, 40000m);

            var detail = RequireQuote(quotes, id);
            Assert.Equal(detail.Balance, 0m, "saldo tras bajar el precio hasta lo cobrado");
            Assert.False(detail.HasCredit, "no tendría que quedar plata a favor.");
        });

        run("Pagos: tampoco desde Proyectos, que es el otro lugar donde se escribe el precio", () =>
        {
            var id = NewCalculatedQuote(quotes, inventory, "Ropero renegociado", "Cliente de taller");
            payments.RegisterPayment(id, PaymentKind.Deposit, 40000m, PaymentMethod.Transfer);

            Assert.Throws(
                () => projects.Update(id, "Ropero renegociado", "Cliente de taller", null, 30000m),
                "Ya se cobraron");

            // Y dejarlo sin precio es lo mismo: la seña quedaría sin nada contra qué medirse.
            Assert.Throws(
                () => projects.Update(id, "Ropero renegociado", "Cliente de taller", null, null),
                "Ya se cobraron");

            Assert.Equal(RequireQuote(quotes, id).Budget ?? 0m, 92500m, "el precio no tendría que moverse");

            projects.Update(id, "Ropero renegociado", "Cliente de taller", null, 50000m);
            Assert.Equal(RequireQuote(quotes, id).Budget ?? 0m, 50000m, "por encima de lo cobrado sí entra");
        });

        run("Pagos: si el recálculo deja plata a favor, el saldo lo dice en vez de mostrar cero", () =>
        {
            // El recálculo automático no se bloquea: corre en cada salida de campo y cortarlo
            // llenaría la pantalla de errores a mitad de la carga. Lo que no puede pasar es
            // que la plata a favor quede escondida detrás de un cero.
            var id = NewCalculatedQuote(quotes, inventory, "Mesa achicada", "Cliente que recortó el trabajo");
            payments.RegisterPayment(id, PaymentKind.Deposit, 90000m, PaymentMethod.Transfer);

            // El cliente recorta el trabajo: menos material, menos días.
            quotes.SaveCalculation(id, 500m, 1m, 10000m, BudgetRates.Defaults());

            var detail = RequireQuote(quotes, id);

            Assert.Equal(detail.Budget ?? 0m, 18625m, "precio recalculado");
            Assert.Equal(detail.Balance, -71375m, "el saldo tiene que quedar en negativo");
            Assert.True(detail.HasCredit, "tendría que figurar como plata a favor del cliente.");
            Assert.Equal(detail.BalanceLabel, "SALDO A FAVOR", "rótulo del saldo");
            Assert.True(
                detail.BalanceDisplay.Contains("71.375", StringComparison.Ordinal),
                $"el importe se muestra en positivo: «{detail.BalanceDisplay}»");
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

    /// <summary>Para que el mensaje de una aserción diga con qué condiciones falló.</summary>
    private static string Describe(CommercialTerms terms)
    {
        if (terms.IsEmpty)
        {
            return "sin condiciones";
        }

        var parts = new List<string>();

        if (terms.DiscountValue > 0)
        {
            parts.Add(terms.DiscountMode == DiscountMode.Percentage
                ? $"descuento {terms.DiscountValue}%"
                : $"descuento ${terms.DiscountValue}");
        }

        if (terms.VatPercent is > 0)
        {
            parts.Add($"IVA {terms.VatPercent}%");
        }

        return string.Join(" + ", parts);
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
