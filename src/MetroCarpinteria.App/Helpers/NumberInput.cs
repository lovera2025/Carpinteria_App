using System.Globalization;

namespace MetroCarpinteria.App.Helpers;

/// <summary>
/// Parseo de números tipeados a mano, con reglas propias en vez de delegar en la cultura.
/// <para>
/// El motivo: antes esto probaba <see cref="CultureInfo.CurrentCulture"/> primero y recién
/// después la invariante. En es-AR el punto es separador de miles, así que tipear
/// <c>"2.5"</c> en una cantidad — que es como se escribe naturalmente en el teclado
/// numérico — devolvía <b>25</b>. Y como <see cref="AppCulture"/> formatea siempre con
/// coma, en una PC configurada en inglés el ciclo mostrar → releer multiplicaba por diez
/// solo, sin que nadie tocara nada.
/// </para>
/// <para>
/// Por eso hay dos contratos distintos, no uno: una cantidad y un importe se escriben
/// diferente. <c>"1.234"</c> como metros de tabla es <b>1,234</b>; como precio es
/// <b>1234</b>. Un parser único no puede acertar en los dos casos, así que cada campo
/// declara cuál usa. La regla no depende de la cultura del sistema: el resultado es el
/// mismo en cualquier máquina.
/// </para>
/// </summary>
public static class NumberInput
{
    /// <summary>
    /// Cantidades, días y porcentajes. Un único separador es <b>siempre</b> decimal:
    /// <c>"2.5"</c> y <c>"2,5"</c> dan 2,5. Nadie cotiza mil doscientas tablas escribiendo
    /// <c>"1.234"</c>, pero sí carga 2.5 metros todo el tiempo.
    /// </summary>
    public static bool TryParseQuantity(string? value, out decimal result) =>
        TryParse(value, groupOnAmbiguity: false, out result);

    /// <summary>
    /// Importes. Un separador seguido de exactamente tres dígitos se toma como agrupación
    /// (<c>"1.234"</c> → 1234), salvo que la parte entera sea cero (<c>"0,500"</c> → 0,5).
    /// </summary>
    public static bool TryParseMoney(string? value, out decimal result) =>
        TryParse(value, groupOnAmbiguity: true, out result);

    /// <summary>
    /// Texto para un campo <b>editable</b>: sin separador de miles y con coma decimal.
    /// Es la inversa exacta de los parsers de arriba, así que el ciclo
    /// mostrar → releer nunca cambia el valor.
    /// <para>
    /// Los helpers de <see cref="AppCulture"/> son para <b>mostrar</b>: agregan separador
    /// de miles y símbolo de moneda, que al releerlos se vuelven ambiguos.
    /// </para>
    /// </summary>
    public static string Format(decimal value) => value.ToString("0.##########", CultureInfo.InvariantCulture)
        .Replace('.', ',');

    public static string Format(decimal? value) => value.HasValue ? Format(value.Value) : string.Empty;

    /// <summary>Parsea una cantidad o tira con un mensaje pensado para mostrarle al usuario.</summary>
    public static decimal ParseQuantityOrThrow(string? value, string fieldName)
    {
        if (!TryParseQuantity(value, out var result))
        {
            throw new InvalidOperationException($"{fieldName} inválido.");
        }

        return result;
    }

    /// <summary>Parsea un importe o tira con un mensaje pensado para mostrarle al usuario.</summary>
    public static decimal ParseMoneyOrThrow(string? value, string fieldName)
    {
        if (!TryParseMoney(value, out var result))
        {
            throw new InvalidOperationException($"{fieldName} inválido.");
        }

        return result;
    }

    public static bool TryParseInt(string? value, out int result)
    {
        result = 0;
        return !string.IsNullOrWhiteSpace(value)
            && int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>
    /// Normaliza a un texto que la cultura invariante entiende y después parsea.
    /// Se hace a mano justamente para no depender de la configuración regional de la PC.
    /// </summary>
    private static bool TryParse(string? value, bool groupOnAmbiguity, out decimal result)
    {
        result = 0m;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Se aceptan espacios (incluido el fino que usan algunas culturas para miles)
        // y el símbolo de moneda, porque es habitual pegar un valor copiado de otro lado.
        var text = value
            .Replace("$", string.Empty)
            .Replace(" ", string.Empty)
            .Replace(" ", string.Empty)
            .Replace(" ", string.Empty)
            .Trim();

        if (text.Length == 0)
        {
            return false;
        }

        var negative = text.StartsWith('-');
        if (negative || text.StartsWith('+'))
        {
            text = text[1..];
        }

        // Sin ningún dígito no hay número: descarta "," y "." sueltos, que si no
        // terminaban normalizados como "0." y parseando a cero.
        if (text.Length == 0
            || !text.Any(char.IsDigit)
            || text.Any(c => !char.IsDigit(c) && c != '.' && c != ','))
        {
            return false;
        }

        var lastDot = text.LastIndexOf('.');
        var lastComma = text.LastIndexOf(',');
        var dots = text.Count(c => c == '.');
        var commas = text.Count(c => c == ',');

        int decimalIndex;

        if (dots > 0 && commas > 0)
        {
            // Están los dos: el que aparece último es el decimal y el otro agrupa.
            // Cubre "1.234.567,89" y "1,234,567.89" sin preguntarle nada a la cultura.
            decimalIndex = Math.Max(lastDot, lastComma);

            // El decimal no puede repetirse, y lo que queda adelante tiene que ser
            // una agrupación real. Sin esto, "1,2,3.4.5" pasaba por 12345.
            var decimalChar = decimalIndex == lastDot ? '.' : ',';
            var groupChar = decimalChar == '.' ? ',' : '.';

            if (text.Count(c => c == decimalChar) > 1 || !IsValidGrouping(text[..decimalIndex], groupChar))
            {
                return false;
            }
        }
        else if (dots + commas == 0)
        {
            decimalIndex = -1;
        }
        else if (dots > 1 || commas > 1)
        {
            // Repetido: solo puede ser separador de miles ("1.000.000").
            if (!IsValidGrouping(text, dots > 1 ? '.' : ','))
            {
                return false;
            }

            decimalIndex = -1;
        }
        else
        {
            var index = dots == 1 ? lastDot : lastComma;
            var digitsAfter = text.Length - index - 1;
            var wholePart = text[..index];

            // El caso ambiguo real: un separador con tres dígitos detrás. Como importe
            // "1.234" son mil doscientos treinta y cuatro pesos; como cantidad son 1,234
            // unidades. La excepción del cero mantiene "0,500" en medio litro y hace que
            // Format() pueda releerse siempre igual.
            var isGrouping = groupOnAmbiguity
                && digitsAfter == 3
                && wholePart.Length is > 0 and <= 3
                && wholePart.TrimStart('0').Length > 0;

            decimalIndex = isGrouping ? -1 : index;
        }

        string normalized;
        if (decimalIndex < 0)
        {
            normalized = text.Replace(".", string.Empty).Replace(",", string.Empty);
        }
        else
        {
            var whole = text[..decimalIndex].Replace(".", string.Empty).Replace(",", string.Empty);
            var fraction = text[(decimalIndex + 1)..];

            if (fraction.Contains('.') || fraction.Contains(','))
            {
                return false;
            }

            normalized = $"{(whole.Length == 0 ? "0" : whole)}.{fraction}";
        }

        if (normalized.Length == 0 || normalized == ".")
        {
            return false;
        }

        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out result))
        {
            return false;
        }

        if (negative)
        {
            result = -result;
        }

        return true;
    }

    /// <summary>
    /// Un separador de miles legítimo deja grupos de tres, con uno a tres dígitos adelante.
    /// Sirve para descartar entradas como <c>"1,2,3.4"</c>, que si no se colaban como 123,4.
    /// </summary>
    private static bool IsValidGrouping(string wholePart, char groupChar)
    {
        var groups = wholePart.Split(groupChar);

        if (groups.Length == 1)
        {
            return groups[0].Length > 0;
        }

        if (groups[0].Length is < 1 or > 3)
        {
            return false;
        }

        return groups.Skip(1).All(g => g.Length == 3);
    }
}
