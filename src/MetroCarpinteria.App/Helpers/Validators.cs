namespace MetroCarpinteria.App.Helpers;

/// <summary>
/// Reglas de validación de formularios, con los mensajes en un solo lugar.
/// <para>
/// La validación de los servicios <b>no</b> se reemplaza con esto. Son dos capas con
/// propósitos distintos: el formulario valida para <i>guiar</i> —marcar el campo, explicar
/// qué falta, no dejar guardar a ciegas— y el servicio valida para <i>garantizar</i>, que
/// es lo que protege los datos si algún camino nuevo se olvida de chequear.
/// </para>
/// <para>
/// Los textos viven acá para que las dos capas digan exactamente lo mismo. Antes el
/// servicio decía "El nombre del producto es obligatorio." y la pantalla no decía nada
/// hasta que alguien apretaba Guardar.
/// </para>
/// </summary>
public static class Validators
{
    public static string? Required(string? value, string fieldName) =>
        string.IsNullOrWhiteSpace(value) ? $"{fieldName} es obligatorio." : null;

    public static string? MaxLength(string? value, int max, string fieldName) =>
        value?.Length > max ? $"{fieldName} no puede superar los {max} caracteres." : null;

    /// <summary>Cantidad que puede ser cero (un stock mínimo, por ejemplo).</summary>
    public static string? NonNegativeQuantity(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return $"{fieldName} es obligatorio.";
        }

        if (!NumberInput.TryParseQuantity(value, out var parsed))
        {
            return $"{fieldName} tiene que ser un número. Podés usar coma o punto para los decimales.";
        }

        return parsed < 0 ? $"{fieldName} no puede ser negativo." : null;
    }

    /// <summary>Cantidad que tiene que ser mayor que cero (los metros de una línea).</summary>
    public static string? PositiveQuantity(string? value, string fieldName)
    {
        var error = NonNegativeQuantity(value, fieldName);
        if (error is not null)
        {
            return error;
        }

        NumberInput.TryParseQuantity(value, out var parsed);
        return parsed <= 0 ? $"{fieldName} tiene que ser mayor que cero." : null;
    }

    /// <summary>Importe opcional: vacío es válido, pero si hay algo tiene que ser un número.</summary>
    public static string? OptionalMoney(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!NumberInput.TryParseMoney(value, out var parsed))
        {
            return $"{fieldName} tiene que ser un importe. Ejemplo: 1.250,50";
        }

        return parsed < 0 ? $"{fieldName} no puede ser negativo." : null;
    }

    public static string? RequiredMoney(string? value, string fieldName) =>
        string.IsNullOrWhiteSpace(value)
            ? $"{fieldName} es obligatorio."
            : OptionalMoney(value, fieldName);

    /// <summary>Porcentaje de la calculadora. Se acota arriba para atajar el 1600% por un 16 mal tipeado.</summary>
    public static string? Percent(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return $"{fieldName} es obligatorio.";
        }

        if (!NumberInput.TryParseQuantity(value, out var parsed))
        {
            return $"{fieldName} tiene que ser un número.";
        }

        if (parsed < 0)
        {
            return $"{fieldName} no puede ser negativo.";
        }

        return parsed > 500
            ? $"{fieldName} de {NumberInput.Format(parsed)}% parece un error de tipeo."
            : null;
    }
}
