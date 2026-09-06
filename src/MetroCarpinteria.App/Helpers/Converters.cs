using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MetroCarpinteria.App.Models;

namespace MetroCarpinteria.App.Helpers;

public class SectionEqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not NavigationSection current || values[1] is not NavigationSection selected)
        {
            return false;
        }

        return current == selected;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Marca el elemento de una lista que coincide con la selección actual.
/// <para>
/// Se usa para los grupos de botones de opción de Configuración, donde cada opción es un
/// registro. Compara por igualdad de valor, así que no depende de que las dos referencias
/// sean el mismo objeto.
/// </para>
/// </summary>
public class SelectedOptionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is not null && Equals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string colorString)
        {
            try
            {
                return new System.Windows.Media.BrushConverter().ConvertFromString(colorString)
                       ?? System.Windows.Media.Brushes.Gray;
            }
            catch
            {
                return System.Windows.Media.Brushes.Gray;
            }
        }

        return System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var boolValue = value is true;
        if (parameter is string param && param.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            boolValue = !boolValue;
        }

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Cambia un importe por puntos cuando el modo privado está puesto.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tapa solo lo que es plata.</b> Si el texto no tiene un importe adentro, lo deja
/// pasar tal cual. Eso es lo que permite ponerlo en cualquier binding de la pantalla sin
/// llevarse puesto lo que él sí quiere seguir viendo: cuáles trabajos terminó, de quién es
/// cada uno, si un jornal está saldado. Un modo que tapa todo no se usa.
/// </para>
/// <para>
/// Devuelve puntos y no vacío a propósito: una celda en blanco se lee como un dato que
/// falta, y en una pantalla de plata eso es justo lo que no queremos que piense.
/// </para>
/// <para>
/// Un converter no se vuelve a evaluar solo cuando cambia el modo, así que al prenderlo o
/// apagarlo se recarga la sección que está a la vista. Es lo que ya hace F5.
/// </para>
/// </remarks>
public class PrivacyMaskConverter : IValueConverter
{
    /// <summary>Lo que se ve en lugar del importe. Con espacios, para que no se lea como una palabra.</summary>
    public const string Mask = "● ● ●";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Controls.Ui.IsPrivacyOn && HasMoney(value as string) ? Mask : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>
    /// Si el texto tiene un importe adentro: un signo de peso con cifras detrás.
    /// </summary>
    /// <remarks>
    /// Es la misma regla con la que <see cref="AppCulture.Money"/> los escribe. Un texto
    /// sin plata —«Todo pagado», «Sin operarios»— pasa entero.
    /// </remarks>
    public static bool HasMoney(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        for (var i = text.IndexOf('$'); i >= 0; i = text.IndexOf('$', i + 1))
        {
            var rest = text.AsSpan(i + 1).TrimStart();

            if (rest.Length > 0 && char.IsDigit(rest[0]))
            {
                return true;
            }
        }

        return false;
    }
}
