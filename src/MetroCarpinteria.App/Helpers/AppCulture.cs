using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using MetroCarpinteria.App.Models;

namespace MetroCarpinteria.App.Helpers;

/// <summary>
/// Cultura única para mostrar plata y fechas. Antes cada modelo y servicio
/// instanciaba su propio <c>new CultureInfo("es-AR")</c>.
/// <para>
/// <b>Todo lo de acá es para mostrar.</b> Agrega separador de miles y símbolo de moneda,
/// que al releerlos son ambiguos. Para escribir en un campo editable va
/// <see cref="NumberInput.Format(decimal)"/>, que es la inversa exacta de los parsers.
/// </para>
/// </summary>
public static class AppCulture
{
    public static CultureInfo Current { get; } = new("es-AR");

    /// <summary>
    /// Fija es-AR para todo el proceso. Va lo más temprano posible en el arranque,
    /// antes de tocar la base o crear ventanas.
    /// </summary>
    public static void InstallAsProcessCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = Current;
        CultureInfo.DefaultThreadCurrentUICulture = Current;
        Thread.CurrentThread.CurrentCulture = Current;
        Thread.CurrentThread.CurrentUICulture = Current;

        // WPF no usa la cultura del hilo para formatear un binding: usa
        // FrameworkElement.Language, que arranca en en-US pase lo que pase. Sin este
        // override, un StringFormat en XAML muestra "1,234.50" mientras el resto de la
        // app muestra "1.234,50", y las fechas salen en formato mes/día.
        //
        // OverrideMetadata admite una sola llamada por tipo en toda la vida del proceso;
        // la bandera es para que los tests puedan invocar esto más de una vez.
        if (_languageOverridden)
        {
            return;
        }

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(Current.IetfLanguageTag)));

        _languageOverridden = true;
    }

    private static bool _languageOverridden;

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
