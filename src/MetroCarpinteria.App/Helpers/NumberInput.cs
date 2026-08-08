using System.Globalization;

namespace MetroCarpinteria.App.Helpers;

/// <summary>
/// Parseo tolerante de números tipeados por el usuario: acepta coma o punto decimal.
/// Estaba duplicado idéntico en Inventario, Proyectos y Personal.
/// </summary>
public static class NumberInput
{
    public static bool TryParseDecimal(string? value, out decimal result)
    {
        result = 0m;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // La cultura del sistema va primero para respetar cómo tipea el usuario
        // ("1.234" es mil doscientos treinta y cuatro en es-AR).
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result)
            || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>Parsea o tira con un mensaje pensado para mostrarle al usuario.</summary>
    public static decimal ParseOrThrow(string? value, string fieldName)
    {
        if (!TryParseDecimal(value, out var result))
        {
            throw new InvalidOperationException($"{fieldName} inválido.");
        }

        return result;
    }

    public static bool TryParseInt(string? value, out int result)
    {
        result = 0;
        return !string.IsNullOrWhiteSpace(value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out result);
    }
}
