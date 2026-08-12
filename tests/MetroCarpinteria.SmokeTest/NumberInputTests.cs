using System.Globalization;
using MetroCarpinteria.App.Helpers;

namespace MetroCarpinteria.SmokeTest;

/// <summary>
/// Regresión del error de escala más caro de la app: <see cref="AppCulture"/> formateaba
/// siempre con coma decimal, pero el parser probaba la cultura del sistema primero.
/// En es-AR eso convertía <c>"2.5"</c> en <b>25</b>, y en una PC en inglés el ciclo
/// mostrar → releer multiplicaba por diez sin que nadie tocara el campo.
/// </summary>
internal static class NumberInputTests
{
    /// <summary>Las tres culturas cubren los dos convenios de separador y el caso de-DE.</summary>
    private static readonly string[] Cultures = ["es-AR", "en-US", "de-DE"];

    private static readonly decimal[] RoundTripValues =
        [0m, 0.001m, 0.5m, 2.5m, 16m, 100m, 1234.56m, 1000000m, 0.105m];

    public static void Run(Action<string, Action> run)
    {
        run("Números: el parseo no depende de la cultura del sistema", () =>
        {
            ForEachCulture(culture =>
            {
                Assert.True(NumberInput.TryParseQuantity("2.5", out var withDot), $"[{culture}] «2.5» debía parsear.");
                Assert.Equal(withDot, 2.5m, $"[{culture}] «2.5» como cantidad");

                Assert.True(NumberInput.TryParseQuantity("2,5", out var withComma), $"[{culture}] «2,5» debía parsear.");
                Assert.Equal(withComma, 2.5m, $"[{culture}] «2,5» como cantidad");
            });
        });

        run("Números: ida y vuelta por Format nunca cambia el valor", () =>
        {
            ForEachCulture(culture =>
            {
                foreach (var value in RoundTripValues)
                {
                    var text = NumberInput.Format(value);

                    Assert.True(NumberInput.TryParseQuantity(text, out var asQuantity),
                        $"[{culture}] no se pudo releer «{text}» como cantidad.");
                    Assert.Equal(asQuantity, value, $"[{culture}] round-trip de cantidad «{text}»");

                    // Los importes de la app son (18,2); con más decimales el formato
                    // deja de ser reversible como dinero y eso es esperado.
                    if (decimal.Round(value, 2) != value)
                    {
                        continue;
                    }

                    Assert.True(NumberInput.TryParseMoney(text, out var asMoney),
                        $"[{culture}] no se pudo releer «{text}» como importe.");
                    Assert.Equal(asMoney, value, $"[{culture}] round-trip de importe «{text}»");
                }
            });
        });

        run("Números: una cantidad con un separador es siempre decimal", () =>
        {
            // El caso que rompía: cargar 2.5 metros de tabla guardaba 25.
            ExpectQuantity("2.5", 2.5m);
            ExpectQuantity("1.234", 1.234m);
            ExpectQuantity("0.001", 0.001m);
            ExpectQuantity("16,5", 16.5m);

            // Repetido solo puede ser separador de miles.
            ExpectQuantity("1.234.567", 1234567m);
        });

        run("Números: un importe con un separador y tres dígitos agrupa", () =>
        {
            ExpectMoney("1.234", 1234m);
            ExpectMoney("1,234", 1234m);
            ExpectMoney("12,50", 12.50m);
            ExpectMoney("1.000.000", 1000000m);

            // Con los dos separadores, el último manda: cubre los dos convenios.
            ExpectMoney("1.234.567,89", 1234567.89m);
            ExpectMoney("1,234,567.89", 1234567.89m);

            // Excepción del cero, para que Format(0,001) siga siendo reversible.
            ExpectMoney("0,500", 0.5m);
        });

        run("Números: se acepta lo que se pega de otro lado", () =>
        {
            ExpectMoney("$ 287.000,00", 287000m);
            ExpectMoney(" 1500 ", 1500m);
            ExpectQuantity("-2,5", -2.5m);
        });

        run("Números: la basura no pasa por válida", () =>
        {
            foreach (var text in new[] { "", "   ", "abc", "1,2,3.4.5", "12a", "1..2", "," })
            {
                Assert.False(NumberInput.TryParseQuantity(text, out _), $"«{text}» no debía parsear como cantidad.");
                Assert.False(NumberInput.TryParseMoney(text, out _), $"«{text}» no debía parsear como importe.");
            }
        });

        run("Números: Format no mete separador de miles", () =>
        {
            // Con separador de miles el texto vuelve ambiguo al releerlo, que es
            // exactamente lo que hacía AppCulture.Quantity en los campos editables.
            var text = NumberInput.Format(1234567.89m);
            Assert.False(text.Contains('.'), $"Format metió un punto: «{text}».");
            Assert.Equal(text, "1234567,89", "Format de un valor grande");
        });
    }

    private static void ForEachCulture(Action<string> body)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in Cultures)
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                body(name);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static void ExpectQuantity(string text, decimal expected)
    {
        Assert.True(NumberInput.TryParseQuantity(text, out var actual), $"«{text}» debía parsear como cantidad.");
        Assert.Equal(actual, expected, $"cantidad «{text}»");
    }

    private static void ExpectMoney(string text, decimal expected)
    {
        Assert.True(NumberInput.TryParseMoney(text, out var actual), $"«{text}» debía parsear como importe.");
        Assert.Equal(actual, expected, $"importe «{text}»");
    }
}
