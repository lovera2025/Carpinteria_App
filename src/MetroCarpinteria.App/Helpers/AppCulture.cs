using System.Globalization;
using MetroCarpinteria.App.Models;

namespace MetroCarpinteria.App.Helpers;

/// <summary>
/// Cultura única para mostrar plata y fechas. Antes cada modelo y servicio
/// instanciaba su propio <c>new CultureInfo("es-AR")</c>.
/// </summary>
public static class AppCulture
{
    public static CultureInfo Current { get; } = new("es-AR");

    /// <summary>Formato de moneda argentina: <c>$ 287.000,00</c>.</summary>
    public static string Money(decimal value) => value.ToString("C", Current);

    public static string Money(decimal? value) => value.HasValue ? Money(value.Value) : "—";

    public static string Percent(decimal percent) => $"{percent.ToString("0.##", Current)}%";

    /// <summary>
    /// Cantidad sin decimales de relleno: <c>5</c>, no <c>5,00</c>. Los decimales aparecen
    /// solo cuando existen (<c>2,5</c>), que es como se habla de material en el taller.
    /// </summary>
    public static string Quantity(decimal value) => value.ToString("0.##", Current);

    /// <summary>Cantidad con la unidad abreviada: <c>5 u.</c>, <c>2,5 m²</c>.</summary>
    public static string QuantityWithUnit(decimal value, string? unit) =>
        $"{Quantity(value)} {ProductUnits.Abbreviate(unit)}";

    public static string ShortDate(DateTime value) => value.ToString("dd/MM/yyyy", Current);

    public static string DateTimeShort(DateTime value) => value.ToString("dd/MM/yyyy HH:mm", Current);
}
